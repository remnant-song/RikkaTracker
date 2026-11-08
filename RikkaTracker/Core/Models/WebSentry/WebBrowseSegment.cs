using System;

namespace RikkaTracker.Core.Models.WebSentry
{
    public class WebBrowseSegment
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Browser { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public string DisplayName => !string.IsNullOrWhiteSpace(Title) ? Title : Domain;
    }
}
