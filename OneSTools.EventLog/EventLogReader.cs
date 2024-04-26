using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace OneSTools.EventLog
{
    /// <summary>
    ///     Presents methods for reading 1C event log
    /// </summary>
    public class EventLogReader : IDisposable
    {
        private readonly EventLogReaderSettings _settings;
        private bool _disposedValue;
        private LgfReader _lgfReader;
        private ManualResetEvent _lgpChangedCreated;
        private FileSystemWatcher _lgpFilesWatcher;
        private LgpReader _lgpReader;
        private string _newLgpPath = null;

        public EventLogReader(EventLogReaderSettings settings)
        {
            _settings = settings;

            _lgfReader = new LgfReader(Path.Combine(_settings.LogFolder, "1Cv8.lgf"));
            _lgfReader.SetPosition(settings.LgfStartPosition);

            if (settings.LgpFileName != string.Empty)
            {
                var file = Path.Combine(_settings.LogFolder, settings.LgpFileName);

                _lgpReader = new LgpReader(file, settings.TimeZone, _lgfReader, settings.SkipEventsBeforeDate);
                _lgpReader.SetPosition(settings.LgpStartPosition);
            }
        }

        /// <summary>
        ///     Current reader's "lgp" file name
        /// </summary>
        public string LgpFileName => _lgpReader.LgpFileName;

        public void Dispose()
        {
            // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     The behaviour of the method depends on the mode of the reader. In the "live" mode it'll be waiting for an appearing
        ///     of the new event item, otherwise It'll just return null
        /// </summary>
        /// <param name="cancellationToken">Token for interrupting of the reader</param>
        /// <returns></returns>
        public EventLogItem ReadNextEventLogItem(CancellationToken cancellationToken = default)
        {
            if (_lgpReader == null)
                SetNextLgpReader();

            if (_settings.LiveMode && _lgpFilesWatcher == null)
                StartLgpFilesWatcher();

            EventLogItem item = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    item = _lgpReader.ReadNextEventLogItem(cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    item = null;
                    _lgpReader = null;
                    break;
                }

                if (item == null)
                {
                    if (_settings.LiveMode)
                    {

                        _lgpChangedCreated.Reset();

                        Thread.Sleep(_settings.ReadingTimeout);
                        /*var waitHandle = WaitHandle.WaitAny(
                            new[] { _lgpChangedCreated, cancellationToken.WaitHandle }, _settings.ReadingTimeout);*/

                        SetNextLgpReader();

                        //if (_settings.ReadingTimeout != Timeout.Infinite && waitHandle == WaitHandle.WaitTimeout)
                        //  throw new EventLogReaderTimeoutException();

                        _lgpChangedCreated.Reset();

                    }
                    else
                    {
                        var newReader = SetNextLgpReader();

                        if (!newReader)
                            break;
                    }
                }
                else
                {
                    _settings.ItemId++;
                    item.Id = _settings.ItemId;

                    break;
                }
            }

            return item;
        }

        private bool SetNextLgpReader()
        {
            var currentReaderLastWriteDateTime = DateTime.MinValue;
            string item1;

            if (_lgpReader != null)
            {
                /*currentReaderLastWriteDateTime = new FileInfo(_lgpReader.LgpPath).LastWriteTime;

                item1 = Directory.GetFiles(_settings.LogFolder, "*.lgp")
                    .Where(file => IsNewFile(file, currentReaderLastWriteDateTime))
                    .OrderBy(file => file)
                    .FirstOrDefault();*/

                // Note this code ignores skip event before date
                item1 = nextHourFileName(_lgpReader.LgpPath);
            }
            else
            {
                if (_settings.SkipEventsBeforeDate != DateTime.MinValue)
                    currentReaderLastWriteDateTime = _settings.SkipEventsBeforeDate.AddSeconds(-1);

                var filesDateTime = Directory.EnumerateFiles(_settings.LogFolder, "*.lgp")
                    .Select(file => (file, File.GetLastWriteTime(file)))
                    .Where(tuple => _lgpReader == null || tuple.file != _lgpReader.LgpPath)
                    .Where(tuple => tuple.Item2 > currentReaderLastWriteDateTime)
                    .OrderBy(tuple => tuple.Item2)
                    .FirstOrDefault();

                item1 = filesDateTime.file;
            }

            if (string.IsNullOrEmpty(item1))
            {
                return false;
            }

            _lgpReader?.Dispose();
            _lgpReader = null;

            _lgpReader = new LgpReader(item1, _settings.TimeZone, _lgfReader, _settings.SkipEventsBeforeDate);

            return true;
        }

        private string nextHourFileName(string currentFileName)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(currentFileName);

            // Extract the timestamp part from the current file name
            string timestampPart = fileNameWithoutExtension.Substring(0, 14); // Assuming the timestamp length is 14 characters

            // Parse the timestamp to get the DateTime
            if (DateTime.TryParseExact(timestampPart, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out DateTime currentFileDateTime))
            {
                // Calculate the date and time for one hour later
                DateTime oneHourLater = currentFileDateTime.AddHours(1);

                // Format the date and time one hour later in the specified format
                string oneHourLaterFormatted = oneHourLater.ToString("yyyyMMddHHmmss") + ".lpg";

                // Construct the expected file name pattern for one hour later
                if (File.Exists(oneHourLaterFormatted))
                    return oneHourLaterFormatted;
            }

            return currentFileName;
        }
        /*private bool IsNewFile(string filePath, DateTime lastWriteDateTime)
        {
            // Extract the timestamp part of the filename (e.g., "20230821000000")
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            if (fileNameWithoutExtension != null && fileNameWithoutExtension.Length >= 14)
            {
                string timestampStr = fileNameWithoutExtension.Substring(0, 14);

                if (DateTime.TryParseExact(timestampStr, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDateTime))
                {
                    return fileDateTime > lastWriteDateTime;
                }
            }

            return false;
        }*/

        private void StartLgpFilesWatcher()
        {
            _lgpChangedCreated = new ManualResetEvent(false);

            _lgpFilesWatcher = new FileSystemWatcher(_settings.LogFolder, "*.lgp")
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes
            };
            _lgpFilesWatcher.Changed += LgpFilesWatcher_EventChanged;
            _lgpFilesWatcher.Created += LgpFilesWatcher_EventCreated;
            _lgpFilesWatcher.EnableRaisingEvents = true;
        }

        private void LgpFilesWatcher_EventChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed)
                _lgpChangedCreated.Set();
        }

        private void LgpFilesWatcher_EventCreated(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Created)
            {

                /*Console.WriteLine("Event Created");
                Console.WriteLine("old path " + _lgpReader.LgpPath);
                Console.WriteLine("new path " + e.FullPath);*/

                //_newLgpPath = e.FullPath;
                //SetNextLgpReader();
                _lgpChangedCreated.Set();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                _lgpFilesWatcher?.Dispose();
                _lgpFilesWatcher = null;
                _lgpChangedCreated?.Dispose();
                _lgpChangedCreated = null;
                _lgfReader?.Dispose();
                _lgfReader = null;
                _lgpReader?.Dispose();
                _lgpReader = null;

                _disposedValue = true;
            }
        }

        ~EventLogReader()
        {
            Dispose(false);
        }
    }
}
