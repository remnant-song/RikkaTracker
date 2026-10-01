using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RikkaTracker.Core.Data;
using RikkaTracker.Core.Models.WebSentry;

namespace RikkaTracker.Services
{
    public class WebDataService : IWebDataService
    {
        private readonly SqliteDbContext _dbContext;

        public WebDataService(SqliteDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task SaveSegmentAsync(WebBrowseSegment segment)
        {
            using var connection = _dbContext.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO WebBrowseLog (Url, Domain, Title, Icon, Browser, StartTime, EndTime)
                VALUES ($url, $domain, $title, $icon, $browser, $startTime, $endTime)
            ";
            command.Parameters.AddWithValue("$url", segment.Url);
            command.Parameters.AddWithValue("$domain", segment.Domain);
            command.Parameters.AddWithValue("$title", segment.Title);
            command.Parameters.AddWithValue("$icon", segment.Icon ?? string.Empty);
            command.Parameters.AddWithValue("$browser", segment.Browser ?? string.Empty);
            command.Parameters.AddWithValue("$startTime", segment.StartTime.ToString("o"));
            command.Parameters.AddWithValue("$endTime", segment.EndTime.ToString("o"));
            await command.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<WebBrowseSegment>> GetSegmentsAsync(DateTime start, DateTime end)
        {
            var segments = new List<WebBrowseSegment>();
            using var connection = _dbContext.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, Url, Domain, Title, Icon, Browser, StartTime, EndTime
                FROM WebBrowseLog
                WHERE StartTime >= $from AND StartTime <= $to
                ORDER BY StartTime ASC
            ";
            command.Parameters.AddWithValue("$from", start.ToString("o"));
            command.Parameters.AddWithValue("$to", end.ToString("o"));

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                segments.Add(new WebBrowseSegment
                {
                    Id = reader.GetInt32(0),
                    Url = reader.GetString(1),
                    Domain = reader.GetString(2),
                    Title = reader.GetString(3),
                    Icon = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Browser = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    StartTime = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
                    EndTime = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind)
                });
            }
            return segments;
        }

        public async Task<IEnumerable<(string Domain, string Title, string Icon, TimeSpan TotalTime)>> GetTopSitesAsync(DateTime start, DateTime end)
        {
            var result = new List<(string Domain, string Title, string Icon, TimeSpan TotalTime)>();
            using var connection = _dbContext.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Domain, Title, Icon, SUM(strftime('%s', EndTime) - strftime('%s', StartTime)) as TotalSeconds
                FROM WebBrowseLog
                WHERE StartTime >= $from AND StartTime <= $to
                GROUP BY Domain
                ORDER BY TotalSeconds DESC
            ";
            command.Parameters.AddWithValue("$from", start.ToString("o"));
            command.Parameters.AddWithValue("$to", end.ToString("o"));

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string domain = reader.GetString(0);
                string title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string icon = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                double seconds = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                result.Add((domain, title, icon, TimeSpan.FromSeconds(seconds)));
            }
            return result;
        }

        public async Task<IEnumerable<(int Hour, TimeSpan TotalTime)>> GetHourlyUsageAsync(DateTime date)
        {
            var hourlyData = new double[24];
            DateTime dayStart = date.Date;
            DateTime dayEnd = dayStart.AddDays(1);

            using var connection = _dbContext.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT StartTime, EndTime
                FROM WebBrowseLog
                WHERE StartTime >= $from AND StartTime < $to
            ";
            command.Parameters.AddWithValue("$from", dayStart.ToString("o"));
            command.Parameters.AddWithValue("$to", dayEnd.ToString("o"));

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                DateTime segStart = DateTime.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind);
                DateTime segEnd = DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind);

                for (int h = 0; h < 24; h++)
                {
                    DateTime hourStart = dayStart.AddHours(h);
                    DateTime hourEnd = hourStart.AddHours(1);

                    DateTime overlapStart = segStart > hourStart ? segStart : hourStart;
                    DateTime overlapEnd = segEnd < hourEnd ? segEnd : hourEnd;

                    if (overlapStart < overlapEnd)
                        hourlyData[h] += (overlapEnd - overlapStart).TotalSeconds;
                }
            }

            return Enumerable.Range(0, 24).Select(h => (h, TimeSpan.FromSeconds(hourlyData[h])));
        }

        public async Task UpdateDomainFaviconAsync(string domain, string localPath)
        {
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(localPath))
                return;

            using var connection = _dbContext.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE WebBrowseLog SET Icon = $path WHERE Domain = $domain";
            command.Parameters.AddWithValue("$path", localPath);
            command.Parameters.AddWithValue("$domain", domain);
            int rows = await command.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($"[Favicon] DB UPDATE: domain='{domain}', path='{localPath}', rows affected={rows}");
        }
    }
}
