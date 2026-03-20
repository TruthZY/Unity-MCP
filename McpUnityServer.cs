using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace McpUnity
{
    /// <summary>
    /// MCP Unity WebSocket服务器
    /// 运行在Unity编辑器中，接收外部MCP服务的命令
    /// </summary>
    public class McpUnityServer : EditorWindow
    {
        private HttpListener _listener;
        private Thread _serverThread;
        private bool _isRunning;
        private string _status = "Stopped";
        private Vector2 _logScrollPosition;
        private List<string> _logs = new List<string>();
        private const int MaxLogs = 100;
        
        // 异步处理相关
        private readonly Queue<PendingRequest> _pendingRequests = new Queue<PendingRequest>();
        private readonly object _queueLock = new object();

        [MenuItem("MCP/Server")]
        public static void ShowWindow()
        {
            GetWindow<McpUnityServer>("MCP Server");
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLogMessage;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
            EditorApplication.update -= OnEditorUpdate;
            StopServer();
        }

        /// <summary>
        /// 每帧检查待处理的请求 - 非阻塞方式
        /// </summary>
        private void OnEditorUpdate()
        {
            lock (_queueLock)
            {
                while (_pendingRequests.Count > 0)
                {
                    var request = _pendingRequests.Dequeue();
                    try
                    {
                        request.Process();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error processing request: {ex.Message}");
                        request.SetError(ex.Message);
                    }
                }
            }
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            string prefix = type switch
            {
                LogType.Error => "[ERROR]",
                LogType.Warning => "[WARN]",
                _ => "[INFO]"
            };
            AddLog($"{prefix} {condition}");
        }

        private void AddLog(string message)
        {
            _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (_logs.Count > MaxLogs)
                _logs.RemoveAt(0);
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            // 状态显示
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", GUILayout.Width(50));
            GUI.color = _isRunning ? Color.green : Color.red;
            EditorGUILayout.LabelField(_status, EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 控制按钮
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isRunning;
            if (GUILayout.Button("Start Server", GUILayout.Height(30)))
            {
                StartServer();
            }
            GUI.enabled = _isRunning;
            if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
            {
                StopServer();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 日志显示
            EditorGUILayout.LabelField("Logs:", EditorStyles.boldLabel);
            _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition, GUILayout.Height(300));
            foreach (var log in _logs)
            {
                EditorGUILayout.LabelField(log, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void StartServer()
        {
            if (_isRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:8090/McpUnity/");
                _listener.Start();

                _isRunning = true;
                _status = "Running (Port 8090)";
                AddLog("Server started on http://localhost:8090/McpUnity/");

                _serverThread = new Thread(ServerLoop)
                {
                    IsBackground = true
                };
                _serverThread.Start();
            }
            catch (Exception ex)
            {
                _status = $"Error: {ex.Message}";
                AddLog($"Failed to start server: {ex.Message}");
            }
        }

        private void StopServer()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            _listener = null;

            _status = "Stopped";
            AddLog("Server stopped");
        }

        private void ServerLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(context));
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Debug.LogError($"Server error: {ex.Message}");
                    }
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // 设置CORS头
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.HttpMethod != "POST")
                {
                    response.StatusCode = 405;
                    response.Close();
                    return;
                }

                // 读取请求体
                string requestBody;
                using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = reader.ReadToEnd();
                }

                Debug.Log($"Received: {requestBody}");

                // 创建待处理请求并加入队列 - 非阻塞方式
                var pendingRequest = new PendingRequest(context, requestBody);
                lock (_queueLock)
                {
                    _pendingRequests.Enqueue(pendingRequest);
                }
                
                // 使用异步等待，不阻塞线程
                pendingRequest.WaitForCompletionAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Request processing error: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// 待处理请求类 - 用于主线程和工作线程间协调
        /// </summary>
        private class PendingRequest
        {
            private readonly HttpListenerContext _context;
            private readonly string _requestBody;
            private string _result;
            private bool _isCompleted;
            private readonly ManualResetEventSlim _completionEvent;

            public PendingRequest(HttpListenerContext context, string requestBody)
            {
                _context = context;
                _requestBody = requestBody;
                _completionEvent = new ManualResetEventSlim(false);
            }

            /// <summary>
            /// 在主线程执行命令处理
            /// </summary>
            public void Process()
            {
                try
                {
                    _result = CommandProcessor.Process(_requestBody);
                }
                catch (Exception ex)
                {
                    _result = $"{{\"error\":\"{ex.Message}\"}}";
                }
                _isCompleted = true;
                _completionEvent.Set();
            }

            public void SetError(string error)
            {
                _result = $"{{\"error\":\"{error}\"}}";
                _isCompleted = true;
                _completionEvent.Set();
            }

            /// <summary>
            /// 异步等待完成并发送响应
            /// </summary>
            public async void WaitForCompletionAsync()
            {
                // 在后台线程等待完成信号
                await Task.Run(() =>
                {
                    // 等待最多10秒
                    if (!_completionEvent.Wait(TimeSpan.FromSeconds(10)))
                    {
                        _result = "{\"error\":\"Request timeout\"}";
                    }
                });

                // 发送响应
                try
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(_result ?? "{}");
                    _context.Response.ContentType = "application/json";
                    _context.Response.ContentLength64 = buffer.Length;
                    await _context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    _context.Response.Close();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error sending response: {ex.Message}");
                    try
                    {
                        _context.Response.StatusCode = 500;
                        _context.Response.Close();
                    }
                    catch { }
                }
                finally
                {
                    _completionEvent.Dispose();
                }
            }
        }
    }
}
