﻿using NodaTime;
using System;
using System.Threading;

namespace OneSTools.EventLog
{
    public class EventLogReaderSettings
    {
        public string LogFolder { get; set; } = "";
        public bool LiveMode { get; set; } = true;
        public string LgpFileName { get; set; } = "";
        public long LgpStartPosition { get; set; } = 0;
        public long LgfStartPosition { get; set; } = 0;
        public long ItemId { get; set; } = 0;
        public int ReadingTimeout { get; set; } = Timeout.Infinite;
        public DateTimeZone TimeZone { get; set; } = DateTimeZoneProviders.Tzdb.GetSystemDefault();
        public DateTime SkipEventsBeforeDate { get; set; }
    }
}