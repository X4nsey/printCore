using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PrintCore.Data
{
    /// <summary>
    /// Прямая обёртка над C API SQLite3 (UTF-8), поддерживающая:
    /// 1) собственную сборку runtimes\win-x86|win-x64\native\e_sqlite3.dll (Windows 7 SP1 — Windows 11);
    /// 2) системную winsqlite3.dll (Windows 10/11) в качестве резервного механизма.
    /// </summary>
    public sealed class SqliteConnection : IDisposable
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int SQLITE_OPEN_READWRITE = 0x00000002;
        private const int SQLITE_OPEN_CREATE = 0x00000004;
        private const int SQLITE_OPEN_FULLMUTEX = 0x00010000;
        private static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        private static bool _nativeLoaded;
        private static bool _useWinSqlite;
        private static readonly object LoadSync = new object();

        private IntPtr _db;

        public SqliteConnection(string dbFilePath)
        {
            EnsureNativeLibraryLoaded();
            var dir = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var rc = Open(ToUtf8NullTerminated(dbFilePath), out _db,
                SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
            if (rc != SQLITE_OK)
            {
                var msg = GetErrorMessage(_db);
                if (_db != IntPtr.Zero) Close(_db);
                _db = IntPtr.Zero;
                throw new InvalidOperationException("Не удалось открыть SQLite (" + rc + "): " + msg);
            }
        }

        public void ExecuteNonQuery(string sql, IDictionary<string, object> parameters = null)
        {
            using (var stmt = Prepare(sql, parameters))
            {
                stmt.StepDone();
            }
        }

        public long ExecuteScalarLong(string sql, IDictionary<string, object> parameters = null)
        {
            using (var stmt = Prepare(sql, parameters))
            {
                return stmt.StepRow() ? stmt.GetInt64(0) : 0L;
            }
        }

        public string ExecuteScalarString(string sql, IDictionary<string, object> parameters = null)
        {
            using (var stmt = Prepare(sql, parameters))
            {
                return stmt.StepRow() ? stmt.GetString(0) : null;
            }
        }

        public List<T> Query<T>(string sql, Func<SqliteStatement, T> map, IDictionary<string, object> parameters = null)
        {
            var list = new List<T>();
            using (var stmt = Prepare(sql, parameters))
            {
                while (stmt.StepRow())
                {
                    list.Add(map(stmt));
                }
            }

            return list;
        }

        public long LastInsertRowId()
        {
            return _useWinSqlite ? WinSqlite.sqlite3_last_insert_rowid(_db) : ESqlite.sqlite3_last_insert_rowid(_db);
        }

        public SqliteStatement Prepare(string sql, IDictionary<string, object> parameters = null)
        {
            var utf8 = ToUtf8NullTerminated(sql);
            IntPtr stmtPtr;
            var rc = _useWinSqlite
                ? WinSqlite.sqlite3_prepare_v2(_db, utf8, utf8.Length - 1, out stmtPtr, IntPtr.Zero)
                : ESqlite.sqlite3_prepare_v2(_db, utf8, utf8.Length - 1, out stmtPtr, IntPtr.Zero);

            if (rc != SQLITE_OK)
            {
                throw new InvalidOperationException("Ошибка SQL (" + rc + "): " + GetErrorMessage(_db));
            }

            var stmt = new SqliteStatement(_db, stmtPtr);
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    stmt.Bind(kv.Key, kv.Value);
                }
            }

            return stmt;
        }

        public void Dispose()
        {
            if (_db != IntPtr.Zero)
            {
                Close(_db);
                _db = IntPtr.Zero;
            }
        }

        private static void EnsureNativeLibraryLoaded()
        {
            if (_nativeLoaded) return;
            lock (LoadSync)
            {
                if (_nativeLoaded) return;

                var arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidate = Path.Combine(baseDir, "runtimes", arch, "native", "e_sqlite3.dll");
                if (File.Exists(candidate) && LoadLibrary(candidate) != IntPtr.Zero)
                {
                    _useWinSqlite = false;
                    _nativeLoaded = true;
                    return;
                }

                // Резервный механизм: системная winsqlite3.dll в Windows 10/11
                var sysPath = Path.Combine(Environment.SystemDirectory, "winsqlite3.dll");
                if (File.Exists(sysPath) && LoadLibrary(sysPath) != IntPtr.Zero)
                {
                    _useWinSqlite = true;
                    _nativeLoaded = true;
                    return;
                }

                throw new DllNotFoundException("Не найдена библиотека e_sqlite3.dll или winsqlite3.dll.");
            }
        }

        private static int Open(byte[] filename, out IntPtr db, int flags, IntPtr vfs)
        {
            return _useWinSqlite
                ? WinSqlite.sqlite3_open_v2(filename, out db, flags, vfs)
                : ESqlite.sqlite3_open_v2(filename, out db, flags, vfs);
        }

        private static void Close(IntPtr db)
        {
            if (_useWinSqlite) WinSqlite.sqlite3_close_v2(db);
            else ESqlite.sqlite3_close_v2(db);
        }

        internal static string GetErrorMessage(IntPtr db)
        {
            if (db == IntPtr.Zero) return "unknown";
            var ptr = _useWinSqlite ? WinSqlite.sqlite3_errmsg(db) : ESqlite.sqlite3_errmsg(db);
            return PtrToUtf8String(ptr);
        }

        internal static byte[] ToUtf8NullTerminated(string text)
        {
            if (text == null) return new byte[] { 0 };
            var count = Encoding.UTF8.GetByteCount(text);
            var buffer = new byte[count + 1];
            Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0);
            return buffer;
        }

        internal static string PtrToUtf8String(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            var len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            var bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return Encoding.UTF8.GetString(bytes);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        public sealed class SqliteStatement : IDisposable
        {
            private readonly IntPtr _db;
            private IntPtr _stmt;

            internal SqliteStatement(IntPtr db, IntPtr stmt)
            {
                _db = db;
                _stmt = stmt;
            }

            public void Bind(string name, object value)
            {
                var paramName = name.StartsWith("@", StringComparison.Ordinal) ? name : "@" + name;
                var idx = _useWinSqlite
                    ? WinSqlite.sqlite3_bind_parameter_index(_stmt, ToUtf8NullTerminated(paramName))
                    : ESqlite.sqlite3_bind_parameter_index(_stmt, ToUtf8NullTerminated(paramName));
                if (idx <= 0) return;

                if (value == null || value == DBNull.Value)
                {
                    if (_useWinSqlite) WinSqlite.sqlite3_bind_null(_stmt, idx);
                    else ESqlite.sqlite3_bind_null(_stmt, idx);
                }
                else if (value is int || value is long || value is short || value is byte || value is bool || value is Enum)
                {
                    var longVal = value is bool b ? (b ? 1L : 0L) : Convert.ToInt64(value);
                    if (_useWinSqlite) WinSqlite.sqlite3_bind_int64(_stmt, idx, longVal);
                    else ESqlite.sqlite3_bind_int64(_stmt, idx, longVal);
                }
                else
                {
                    var str = value is DateTime dt ? dt.ToString("O") : value.ToString();
                    var bytes = Encoding.UTF8.GetBytes(str);
                    if (_useWinSqlite) WinSqlite.sqlite3_bind_text(_stmt, idx, bytes, bytes.Length, SQLITE_TRANSIENT);
                    else ESqlite.sqlite3_bind_text(_stmt, idx, bytes, bytes.Length, SQLITE_TRANSIENT);
                }
            }

            public bool StepRow()
            {
                var rc = _useWinSqlite ? WinSqlite.sqlite3_step(_stmt) : ESqlite.sqlite3_step(_stmt);
                if (rc == SQLITE_ROW) return true;
                if (rc == SQLITE_DONE) return false;
                throw new InvalidOperationException("Ошибка выполнения SQL (" + rc + "): " + GetErrorMessage(_db));
            }

            public void StepDone()
            {
                var rc = _useWinSqlite ? WinSqlite.sqlite3_step(_stmt) : ESqlite.sqlite3_step(_stmt);
                if (rc != SQLITE_DONE && rc != SQLITE_ROW)
                {
                    throw new InvalidOperationException("Ошибка выполнения SQL (" + rc + "): " + GetErrorMessage(_db));
                }
            }

            public long GetInt64(int col)
            {
                return _useWinSqlite ? WinSqlite.sqlite3_column_int64(_stmt, col) : ESqlite.sqlite3_column_int64(_stmt, col);
            }

            public int GetInt32(int col)
            {
                return (int)GetInt64(col);
            }

            public string GetString(int col)
            {
                var ptr = _useWinSqlite ? WinSqlite.sqlite3_column_text(_stmt, col) : ESqlite.sqlite3_column_text(_stmt, col);
                return PtrToUtf8String(ptr);
            }

            public void Dispose()
            {
                if (_stmt != IntPtr.Zero)
                {
                    if (_useWinSqlite) WinSqlite.sqlite3_finalize(_stmt);
                    else ESqlite.sqlite3_finalize(_stmt);
                    _stmt = IntPtr.Zero;
                }
            }
        }

        private static class ESqlite
        {
            private const string Dll = "e_sqlite3";
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_step(IntPtr stmt);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_parameter_index(IntPtr stmt, byte[] zName);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_null(IntPtr stmt, int index);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern long sqlite3_column_int64(IntPtr stmt, int iCol);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern long sqlite3_last_insert_rowid(IntPtr db);
        }

        private static class WinSqlite
        {
            private const string Dll = "winsqlite3";
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_step(IntPtr stmt);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_parameter_index(IntPtr stmt, byte[] zName);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_null(IntPtr stmt, int index);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int n, IntPtr destructor);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern long sqlite3_column_int64(IntPtr stmt, int iCol);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);
            [DllImport(Dll, CallingConvention = CallingConvention.StdCall)] public static extern long sqlite3_last_insert_rowid(IntPtr db);
        }
    }
}
