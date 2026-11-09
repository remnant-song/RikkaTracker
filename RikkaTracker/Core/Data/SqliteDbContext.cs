using System;
using System.IO;
using Microsoft.Data.Sqlite;
using RikkaTracker.Services;

namespace RikkaTracker.Core.Data
{
    public class SqliteDbContext
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public SqliteDbContext(IConfigService configService)
        {
            _dbPath = Path.Combine(configService.Config.DataStoragePath, "tracker.db");
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={_dbPath};";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Performance optimizations
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    PRAGMA journal_mode=WAL;
                    PRAGMA synchronous=NORMAL;
                    PRAGMA temp_store=MEMORY;
                ";
                command.ExecuteNonQuery();
            }

            // Create tables
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ActivityLog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProcessName TEXT NOT NULL,
                        ProcessPath TEXT,
                        WindowTitle TEXT,
                        Alias TEXT,
                        Status INTEGER NOT NULL,
                        StartTime TEXT NOT NULL,
                        EndTime TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_Log_Start ON ActivityLog(StartTime);
                    CREATE INDEX IF NOT EXISTS IX_Log_Process_Start ON ActivityLog(ProcessName, StartTime);
                    CREATE INDEX IF NOT EXISTS IX_Log_Process_Path_Start ON ActivityLog(ProcessPath, StartTime);

                    CREATE TABLE IF NOT EXISTS WebBrowseLog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Url TEXT NOT NULL,
                        Domain TEXT NOT NULL,
                        Title TEXT NOT NULL,
                        Icon TEXT,
                        Browser TEXT NOT NULL DEFAULT '',
                        StartTime TEXT NOT NULL,
                        EndTime TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_WebLog_Start ON WebBrowseLog(StartTime);
                    CREATE INDEX IF NOT EXISTS IX_WebLog_Domain_Start ON WebBrowseLog(Domain, StartTime);
                ";
                command.ExecuteNonQuery();
            }

            // Migrate databases created before the Browser column existed
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('WebBrowseLog') WHERE name='Browser'";
                long exists = (long)command.ExecuteScalar();
                if (exists == 0)
                {
                    using var migrate = connection.CreateCommand();
                    migrate.CommandText = "ALTER TABLE WebBrowseLog ADD COLUMN Browser TEXT NOT NULL DEFAULT ''";
                    migrate.ExecuteNonQuery();
                }
            }
        }

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
