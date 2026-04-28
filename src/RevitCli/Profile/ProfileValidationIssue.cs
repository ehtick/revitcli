using System;

namespace RevitCli.Profile;

/// <summary>
/// One issue produced by <see cref="ProfileValidator"/>. Severity drives the
/// process exit code: any <see cref="ProfileValidationSeverity.Error"/> entry
/// causes <c>revitcli profile validate</c> to exit non-zero.
/// </summary>
public sealed class ProfileValidationIssue
{
    public ProfileValidationIssue(ProfileValidationSeverity severity, string path, string message)
    {
        Severity = severity;
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>Severity bucket — <c>error</c> fails the gate, <c>warning</c> / <c>info</c> do not.</summary>
    public ProfileValidationSeverity Severity { get; }

    /// <summary>Dotted path into the profile (e.g. <c>publish.default.precheck</c>).</summary>
    public string Path { get; }

    /// <summary>Human-readable description of what is wrong.</summary>
    public string Message { get; }

    public override string ToString() => $"[{Severity.ToString().ToLowerInvariant()}] {Path}: {Message}";
}

public enum ProfileValidationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}
