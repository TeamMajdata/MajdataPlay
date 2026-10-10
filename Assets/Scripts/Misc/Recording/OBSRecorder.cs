#nullable enable
using MajdataPlay.IO.Storage;
using MajdataPlay.Diagnostics;
using MajdataPlay.Utils;
using System;
using System.IO;
using System.Threading.Tasks;
using WebSocketSharp;

namespace MajdataPlay.Recording
{
    public class OBSRecorder : IRecorder
    {
        private WebSocket _webSocket = new("ws://127.0.0.1:4455");
        private bool _disposed = false;
        string _name = "";
        string _obsOutPath = "";
        public bool IsConnected { get; set; } = false;
        public bool IsRecording { get; set; } = false;

        private const string StartRecordMessage = @"{
                    ""op"": 6,
                    ""d"": {
                        ""requestType"": ""StartRecord"",
                        ""requestId"": ""start_recording""
                    }
                }";

        private const string StopRecordMessage = @"{
                    ""op"": 6,
                    ""d"": {
                        ""requestType"": ""StopRecord"",
                        ""requestId"": ""stop_recording""
                    }
                }";

        private const string AuthenticateMessage = @"{
                    ""op"": 1,
                    ""d"": {
                        ""rpcVersion"": 1
                    }
                }";

        public OBSRecorder() => Init();

        public void Init()
        {
            _webSocket.OnMessage += OnMessageReceived;
            try
            {
                Connect();
                Authenticate();
            }
            catch
            {
                IsConnected = false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                Disconnect();
                _webSocket = null;
            }

            _disposed = true;
        }

        ~OBSRecorder() => Dispose(false);

        private void Connect() => _webSocket.Connect();

        private void Disconnect()
        {
            _webSocket.Close();
            IsConnected = false;
        }

        public void StartRecord() => _webSocket.Send(StartRecordMessage);
        public async Task StartRecordAsync()
        {
            try
            {
                while (!IsConnected)
                {
                    Authenticate();
                    await Task.Delay(1000);
                }
            }
            catch
            {
                throw new OBSRecorderException();
            }

            await Task.Run(StartRecord);
        }
        public void StopRecord() => _webSocket.Send(StopRecordMessage);
        public async Task StopRecordAsync()
        {
            if (!IsConnected || !IsRecording) return;
            await Task.Run(StopRecord);
        }
        private void Authenticate() => _webSocket.Send(AuthenticateMessage);
        public void OnLateUpdate()
        {

        }

        public void SetOutputName(string name)
        {
            _name = name;
        }
        private void OnMessageReceived(object sender, MessageEventArgs e)
        {
            try
            {
                var message = Serializer.Json.Deserialize<ReceivedMessage>(e.Data);
                MajDebug.LogInfo("[OBS] Received: " + e.Data);
                switch (message.op)
                {
                    case 2: // Identified
                        {
                            IsConnected = true;
                            break;
                        }
                    case 7: // RequestResponse
                        {
                            if (message.d.requestType == "StartRecord")
                            {
                                if (message.d.requestStatus.result)
                                {
                                    IsRecording = true;
                                }
                                else
                                {
                                    MajDebug.LogInfo("[OBS] Start Record Failed.");
                                }
                            }

                            if (message.d.requestType == "StopRecord")
                            {
                                if (message.d.requestStatus.result)
                                {
                                    IsRecording = false;
                                    _obsOutPath = message.d.responseData.outputPath;
                                    MajDebug.LogInfo("[OBS] Record Saved To " + message.d.responseData.outputPath);
                                }
                                else
                                {
                                    MajDebug.LogInfo("[OBS] Stop Record Failed.");
                                }
                            }
                            break;
                        }
                    case 5:
                        {
                            //we move the file after record finishs
                            if (message.d.eventType == "RecordStateChanged" && message.d.eventData.outputState == "OBS_WEBSOCKET_OUTPUT_STOPPED")
                            {
                                MajDebug.LogInfo("[OBS] Moving video to game dir");
                                var timestamp = $"{DateTime.Now:yyyy-MM-dd_HH_mm_ss}";
                                var outputPath = Path.Combine(MajEnv.RecordOutputsPath, $"{_name}_{timestamp}.mp4");
                                FileSystem.MoveFile(_obsOutPath, outputPath);
                            }

                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                MajDebug.LogException(ex);
            }
        }

        #region WebSocketMessageClass
        public class RequestStatus
        {
            public int code { get; set; }
            public bool result { get; set; }
        }

        public class ResponseData
        {
            public string outputPath { get; set; }
        }

        public class EventData
        {
            public bool outputActive { get; set; }
            public string outputPath { get; set; }
            public string outputState { get; set; }
        }

        public class Event
        {
            public EventData eventData { get; set; }
            public int eventIntent { get; set; }
            public string eventType { get; set; }
        }

        public class DData
        {
            public string obsWebSocketVersion { get; set; }
            public int rpcVersion { get; set; }
            public int negotiatedRpcVersion { get; set; }
            public string requestId { get; set; }
            public RequestStatus requestStatus { get; set; }
            public string requestType { get; set; }
            public EventData eventData { get; set; }
            public ResponseData responseData { get; set; }
            public int eventIntent { get; set; }
            public string eventType { get; set; }
        }

        public class ReceivedMessage
        {
            public DData d { get; set; }
            public int op { get; set; }
            public override string ToString() => Serializer.Json.Serialize(this);
        }
        #endregion
    }
}
