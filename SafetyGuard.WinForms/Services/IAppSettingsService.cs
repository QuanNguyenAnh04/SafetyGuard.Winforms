using System;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public interface IAppSettingsService
{
    AppSettings Current { get; }
    event Action<AppSettings>? OnChanged;

    void Reload();
    void Save(AppSettings settings);
}
