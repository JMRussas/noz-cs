//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace NoZ.Editor;

public static class GenerationClient
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static void Generate(GenerationRequest request, Action<GenerationStatus> callback,
        CancellationToken cancellationToken = default)
    {
        Task.Run(async () =>
        {
            try
            {
                // Phase 1: Submit job
                var json = JsonSerializer.Serialize(request, _jsonOptions);

                // Debug: write request JSON to tmp folder
                try
                {
                    var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
                    Directory.CreateDirectory(tmpDir);
                    File.WriteAllText(System.IO.Path.Combine(tmpDir, "generation_request.json"), json);
                }
                catch { }

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{request.Server}/generate", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log.Error($"Generation submit failed {response.StatusCode}: {errorBody}");
                    EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                    {
                        State = GenerationState.Failed,
                        Error = $"Submit failed: {response.StatusCode}"
                    }));
                    return;
                }

                var submitJson = await response.Content.ReadAsStringAsync();
                var submitResult = JsonSerializer.Deserialize<GenerationSubmitResponse>(submitJson, _jsonOptions);
                if (submitResult == null || string.IsNullOrEmpty(submitResult.JobId))
                {
                    Log.Error("Generation submit returned no job_id");
                    EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                    {
                        State = GenerationState.Failed,
                        Error = "No job_id returned"
                    }));
                    return;
                }

                var jobId = submitResult.JobId;
                Log.Info($"Generation job submitted: {jobId}");

                EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                {
                    State = GenerationState.Queued,
                    JobId = jobId,
                    QueuePosition = submitResult.Position
                }));

                // Phase 2: Poll loop
                var pollUrl = $"{request.Server}/jobs/{jobId}";
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken);

                    var pollResponse = await _http.GetAsync(pollUrl, cancellationToken);
                    if (!pollResponse.IsSuccessStatusCode)
                    {
                        var errorBody = await pollResponse.Content.ReadAsStringAsync();
                        Log.Error($"Poll failed {pollResponse.StatusCode}: {errorBody}");
                        EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                        {
                            State = GenerationState.Failed,
                            JobId = jobId,
                            Error = $"Poll failed: {pollResponse.StatusCode}"
                        }));
                        return;
                    }

                    var pollJson = await pollResponse.Content.ReadAsStringAsync();
                    var job = JsonSerializer.Deserialize<GenerationJobResponse>(pollJson, _jsonOptions);
                    if (job == null)
                    {
                        Log.Error("Poll returned invalid response");
                        EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                        {
                            State = GenerationState.Failed,
                            JobId = jobId,
                            Error = "Invalid poll response"
                        }));
                        return;
                    }

                    var status = MapJobToStatus(job);
                    EditorApplication.RunOnMainThread(() => callback(status));

                    if (status.IsTerminal)
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                Log.Info("Generation cancelled");
            }
            catch (Exception ex)
            {
                Log.Error($"Generation failed: {ex.Message}");
                EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                {
                    State = GenerationState.Failed,
                    Error = ex.Message
                }));
            }
        }, cancellationToken);
    }

    private static GenerationStatus MapJobToStatus(GenerationJobResponse job)
    {
        var state = job.Status switch
        {
            "queued" => GenerationState.Queued,
            "running" => GenerationState.Running,
            "completed" => GenerationState.Completed,
            "failed" => GenerationState.Failed,
            _ => GenerationState.Failed
        };

        return new GenerationStatus
        {
            State = state,
            JobId = job.JobId,
            QueuePosition = job.QueuePosition ?? 0,
            CurrentNode = job.CurrentNode,
            NodesCompleted = job.NodesCompleted ?? 0,
            TotalNodes = job.TotalNodes ?? 0,
            CurrentStep = job.CurrentStep ?? 0,
            TotalSteps = job.TotalSteps ?? 0,
            Result = job.Result,
            Error = job.Error ?? (state == GenerationState.Failed ? "Unknown error" : null)
        };
    }
}

public class GenerationRequest
{
    [JsonIgnore]
    public string Server { get; set; } = "";

    public string Workflow { get; set; } = "sprite";
    public List<GenerationShape> Shapes { get; set; } = [];
    public GenerationRefine? Refine { get; set; }
    public long? Seed { get; set; }
}

public class GenerationShape
{
    public string Mask { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string? NegativePrompt { get; set; }
    public float Strength { get; set; } = 0.8f;
    public int Steps { get; set; } = 40;
    public float GuidanceScale { get; set; } = 6.0f;
}

public class GenerationRefine
{
    public string Prompt { get; set; } = "";
    public string? NegativePrompt { get; set; }
    public float Strength { get; set; } = 0.64f;
    public int Steps { get; set; } = 40;
    public float GuidanceScale { get; set; } = 6.0f;
}

public class GenerationResponse
{
    public string Image { get; set; } = "";
    public long Seed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public enum GenerationState
{
    Queued,
    Running,
    Completed,
    Failed
}

public class GenerationStatus
{
    public GenerationState State { get; set; }
    public string? JobId { get; set; }

    // Queued
    public int QueuePosition { get; set; }

    // Running
    public string? CurrentNode { get; set; }
    public int NodesCompleted { get; set; }
    public int TotalNodes { get; set; }
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }

    // Completed
    public GenerationResponse? Result { get; set; }

    // Failed
    public string? Error { get; set; }

    public float Progress => TotalSteps > 0 ? (float)CurrentStep / TotalSteps : 0f;
    public bool IsTerminal => State is GenerationState.Completed or GenerationState.Failed;
}

// POST /generate response
public class GenerationSubmitResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public int Position { get; set; }
}

// GET /jobs/{job_id} response
public class GenerationJobResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public int? QueuePosition { get; set; }
    public string? CurrentNode { get; set; }
    public int? NodesCompleted { get; set; }
    public int? TotalNodes { get; set; }
    public int? CurrentStep { get; set; }
    public int? TotalSteps { get; set; }
    public GenerationResponse? Result { get; set; }
    public string? Error { get; set; }
}
