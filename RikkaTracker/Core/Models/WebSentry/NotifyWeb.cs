using System;

namespace RikkaTracker.Core.Models.WebSentry
{
    public class NotifyWeb
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int ActiveTime { get; set; }
        public int Duration { get; set; }
        public string Browser { get; set; } = string.Empty;
        public DateTime ActiveDateTime
        {
            get
            {
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddSeconds(ActiveTime).ToLocalTime();
            }
        }
    }
}
