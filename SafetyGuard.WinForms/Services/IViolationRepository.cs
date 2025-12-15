using System;
using System.Collections.Generic;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public interface IViolationRepository
{
    event Action? OnChanged;

    void Add(ViolationRecord v);
    void UpdateStatus(string id, ViolationStatus status, string? notes = null);

    IReadOnlyList<ViolationRecord> Query(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        ViolationType? type = null,
        ViolationStatus? status = null,
        string? search = null,
        int limit = 2000);

    void DeleteOlderThanUtc(DateTime cutoffUtc);

    // dashboard helpers
    long CountByDateRange(DateTime fromUtc, DateTime toUtc);
    long CountByDateRangeAndLevel(DateTime fromUtc, DateTime toUtc, ViolationLevel level);
}
