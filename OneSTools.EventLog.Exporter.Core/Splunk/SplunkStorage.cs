using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.IO;
using System.Reflection;


namespace OneSTools.EventLog.Exporter.Core.Splunk
{
    public class SplunkStorage : IEventLogStorage
    {
        private readonly ILogger<SplunkStorage> _logger;
        private string _splunkHost;
        private string _splunkToken;
        private string _eventLogPositionPath;
        private string _databaseName;
        string _lastevent = null;
        HttpClient _client;
        private int _maximumRetries = 3;


        // Constructor for manager
        public SplunkStorage(SplunkStorageSetting settings, ILogger<SplunkStorage> logger = null)
        {
            _logger = logger;

            _splunkHost = settings.Host;
            _splunkToken = settings.Token;
            _eventLogPositionPath = Path.Combine(settings.Path, $"eventLogPosition-{settings.DB}.txt");
            _databaseName = settings.DB;

            _client = new HttpClient();
            _client.BaseAddress = new Uri(_splunkHost);
            _client.DefaultRequestHeaders.Add("Authorization", _splunkToken);
            _client.Timeout = TimeSpan.FromSeconds(3);
            CheckSettings();
        }

        // Second constructor for stand alone execution
        public SplunkStorage(ILogger<SplunkStorage> logger, IConfiguration configuration)
        {
            _logger = logger;

            _splunkHost = configuration.GetValue("Splunk:Host", "");
            _splunkToken = configuration.GetValue("Splunk:Token", "");

            // check how to handle if get value is empty ?????
            _eventLogPositionPath = Path.Combine(configuration.GetValue("Splunk:EventLogPositionPath", ""), "eventLogPosition.txt");

            _client = new HttpClient();
            _client.BaseAddress = new Uri(_splunkHost);
            _client.DefaultRequestHeaders.Add("Authorization", _splunkToken);
            _client.Timeout = TimeSpan.FromSeconds(3);
            CheckSettings();
        }

        private void CheckSettings()
        {
            if (string.IsNullOrWhiteSpace(_splunkHost))
                throw new Exception("You must set Splunk Host parameter before starting the exporter");
            if (string.IsNullOrWhiteSpace(_splunkToken))
                throw new Exception("You must set Splunk Token parameter before starting the exporter");
            if (string.IsNullOrWhiteSpace(_eventLogPositionPath))
                throw new Exception("You must set Splunk EventLogPositionPath parameter before starting the exporter");
        }

        public void Dispose()
        {
        }

        public async Task<EventLogPosition> ReadEventLogPositionAsync(CancellationToken cancellationToken = default)
        {
            if (File.Exists(_eventLogPositionPath))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(_eventLogPositionPath))
                    {
                        string json = await reader.ReadToEndAsync();

                        // possible error. Should add exeption handler
                        EventLogItem item = JsonSerializer.Deserialize<EventLogItem>(json)!;

                        if (item.FileName is null)
                            return null;
                        return new EventLogPosition(item.FileName, item.EndPosition, item.LgfEndPosition, item.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError("Can't read event log position: {0}", ex);
                }                
            }     
            return null;
        }

        public async Task WriteEventLogDataAsync(List<EventLogItem> entities, CancellationToken cancellationToken = default)
        {
            // Options for converting to json. Used UnsafeRelaxedJsonEscaping for convertion kyrylic text 
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };

            for(var i = 0; i < entities.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await SavePosition();
                    return;
                }

                // Convert event log item to JSON
                entities[i].DatabaseName = _databaseName;
                string json = JsonSerializer.Serialize(entities[i], options);
                var content = new StringContent("{\"event\": " + json + "}", Encoding.UTF8, "application/json");

                // Send the event to Splunk and check result
                if (await HttpPostAsync(content, cancellationToken))
                {
                    _logger?.LogInformation("Event sent successfully.");
                    _lastevent = json;
                }
                else
                    i--;
            }

            //Write last position to file
            await SavePosition();
        }

        private async Task SavePosition()
        {
            if (_lastevent != null)
            {
                try 
                {
                    using (StreamWriter writer = new StreamWriter(_eventLogPositionPath))
                    {
                        await writer.WriteAsync(_lastevent);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError("Can't write event log position to file: {0}", ex);
                }
            }
        }

        private async Task<bool> HttpPostAsync(StringContent content, CancellationToken cancellationToken = default)
        {
            try
            {
                HttpResponseMessage response = await _client.PostAsync("", content);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogError($"Error sending event. Response content: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger?.LogError("Error sending event. Timed out.");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error sending event. An error occurred: {ex.Message}");
                return false;
            }
            return true;
        }
    }
}