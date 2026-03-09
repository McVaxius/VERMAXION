using System;
using System.Numerics;
using ImGuiNET;
using Dalamud.Interface.Windowing;
using Vermaxion.Services;

namespace Vermaxion.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly VerminionService _service;
    private readonly Configuration _config;

    public MainWindow(VerminionService service, Configuration config) 
        : base("Vermaxion Plugin##MainWindow")
    {
        _service = service;
        _config = config;

        Size = new Vector2(400, 300);
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Vermaxion Plugin");
        ImGui.Separator();
        ImGui.Spacing();

        // Status Display
        ImGui.Text($"Status: {_service.GetStatusText()}");
        ImGui.Text($"Attempts: {_service.CurrentAttempt}/{_service.MaxAttempts}");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Control Buttons
        if (_service.IsRunning)
        {
            if (ImGui.Button("Stop"))
            {
                _service.StopAutomation();
            }
        }
        else
        {
            if (ImGui.Button("Start"))
            {
                _service.StartAutomation();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            _service.ResetAutomation();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Configuration Section
        if (ImGui.CollapsingHeader("Configuration"))
        {
            DrawConfiguration();
        }

        ImGui.Spacing();

        // Debug Section
        if (ImGui.CollapsingHeader("Debug"))
        {
            DrawDebug();
        }
    }

    private void DrawConfiguration()
    {
        ImGui.Indent();

        int maxAttempts = _config.MaxAttempts;
        if (ImGui.InputInt("Max Attempts", ref maxAttempts))
        {
            if (maxAttempts > 0 && maxAttempts <= 100)
            {
                _config.MaxAttempts = maxAttempts;
            }
        }

        bool enableAutoRetainer = _config.EnableAutoRetainer;
        if (ImGui.Checkbox("Enable AutoRetainer", ref enableAutoRetainer))
        {
            _config.EnableAutoRetainer = enableAutoRetainer;
        }

        int queueDelay = _config.QueueRetryDelay;
        if (ImGui.SliderInt("Queue Delay (ms)", ref queueDelay, 1000, 10000))
        {
            _config.QueueRetryDelay = queueDelay;
        }

        int failureDelay = _config.FailureDelay;
        if (ImGui.SliderInt("Failure Delay (ms)", ref failureDelay, 1000, 10000))
        {
            _config.FailureDelay = failureDelay;
        }

        ImGui.Spacing();
        if (ImGui.Button("Save Configuration"))
        {
            _config.Save();
        }

        ImGui.Unindent();
    }

    private void DrawDebug()
    {
        ImGui.Indent();
        ImGui.Text($"Current State: {_service.CurrentState}");
        ImGui.Text($"Is Running: {_service.IsRunning}");
        ImGui.Text($"Current Attempt: {_service.CurrentAttempt}");
        ImGui.Text($"Max Attempts: {_service.MaxAttempts}");
        ImGui.Unindent();
    }
}
