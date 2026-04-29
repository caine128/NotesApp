# Advanced Configuration Patterns

Validators with dependencies, named options, complete production example, and testing configuration validators.

## Contents

- [Validators with Dependencies](#validators-with-dependencies)
- [Named Options](#named-options)
- [Complete Example - Production Settings Class](#complete-example---production-settings-class)
- [Testing Configuration Validators](#testing-configuration-validators)

## Validators with Dependencies

IValidateOptions validators are resolved from DI, so they can have dependencies:

```csharp
public class DatabaseSettingsValidator : IValidateOptions<DatabaseSettings>
{
    private readonly ILogger<DatabaseSettingsValidator> _logger;
    private readonly IHostEnvironment _environment;

    public DatabaseSettingsValidator(
        ILogger<DatabaseSettingsValidator> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, DatabaseSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("ConnectionString is required");
        }

        // Environment-specific validation
        if (_environment.IsProduction())
        {
            if (options.ConnectionString?.Contains("localhost") == true)
            {
                failures.Add("Production cannot use localhost database");
            }

            if (!options.ConnectionString?.Contains("Encrypt=True") == true)
            {
                _logger.LogWarning("Production database connection should use encryption");
            }
        }

        // Validate connection string format
        if (!string.IsNullOrEmpty(options.ConnectionString))
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(options.ConnectionString);
                if (string.IsNullOrEmpty(builder.DataSource))
                {
                    failures.Add("ConnectionString must specify a Data Source");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"ConnectionString is malformed: {ex.Message}");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
```

## Named Options

When you have multiple instances of the same settings type (e.g., multiple database connections):

```csharp
// appsettings.json
{
  "Databases": {
    "Primary": {
      "ConnectionString": "Server=primary;..."
    },
    "Replica": {
      "ConnectionString": "Server=replica;..."
    }
  }
}

// Registration
builder.Services.AddOptions<DatabaseSettings>("Primary")
    .BindConfiguration("Databases:Primary")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<DatabaseSettings>("Replica")
    .BindConfiguration("Databases:Replica")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Consumption
public class DataService
{
    private readonly DatabaseSettings _primary;
    private readonly DatabaseSettings _replica;

    public DataService(IOptionsSnapshot<DatabaseSettings> options)
    {
        _primary = options.Get("Primary");
        _replica = options.Get("Replica");
    }
}
```

### Named Options Validator

```csharp
public class DatabaseSettingsValidator : IValidateOptions<DatabaseSettings>
{
    public ValidateOptionsResult Validate(string? name, DatabaseSettings options)
    {
        var failures = new List<string>();
        var prefix = string.IsNullOrEmpty(name) ? "" : $"[{name}] ";

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add($"{prefix}ConnectionString is required");
        }

        // Name-specific validation
        if (name == "Primary" && options.ReadOnly)
        {
            failures.Add("Primary database cannot be read-only");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
```

## Complete Example - Production Settings Class

This example shows a background worker settings class with cross-property and environment-aware `IValidateOptions<T>`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

public class OutboxSettings
{
    public const string SectionName = "Outbox";

    [Range(1, 3600, ErrorMessage = "PollingIntervalSeconds must be between 1 and 3600")]
    public int PollingIntervalSeconds { get; set; } = 30;

    [Range(1, 500, ErrorMessage = "BatchSize must be between 1 and 500")]
    public int BatchSize { get; set; } = 50;

    [Range(1, 10, ErrorMessage = "MaxRetryAttempts must be between 1 and 10")]
    public int MaxRetryAttempts { get; set; } = 3;

    public bool EnableDeadLetterQueue { get; set; } = true;
}

public class OutboxSettingsValidator : IValidateOptions<OutboxSettings>
{
    private readonly IHostEnvironment _environment;

    public OutboxSettingsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, OutboxSettings options)
    {
        var failures = new List<string>();

        // Cross-property validation: high batch size needs slower polling
        if (options.BatchSize > 100 && options.PollingIntervalSeconds < 5)
        {
            failures.Add(
                "When BatchSize exceeds 100, PollingIntervalSeconds must be at least 5 " +
                "to avoid overwhelming the database.");
        }

        // Environment-specific validation
        if (_environment.IsProduction() && options.PollingIntervalSeconds > 60)
        {
            failures.Add(
                "Production outbox polling interval should not exceed 60 seconds.");
        }

        if (_environment.IsProduction() && !options.EnableDeadLetterQueue)
        {
            failures.Add("Dead letter queue must be enabled in production.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

// Registration
builder.Services.AddOptions<OutboxSettings>()
    .BindConfiguration(OutboxSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<OutboxSettings>, OutboxSettingsValidator>();
```

```json
// appsettings.json
{
  "Outbox": {
    "PollingIntervalSeconds": 30,
    "BatchSize": 50,
    "MaxRetryAttempts": 3,
    "EnableDeadLetterQueue": true
  }
}
```

## Testing Configuration Validators

```csharp
public class SmtpSettingsValidatorTests
{
    private readonly SmtpSettingsValidator _validator = new();

    [Fact]
    public void Validate_WithValidSettings_ReturnsSuccess()
    {
        var settings = new SmtpSettings
        {
            Host = "smtp.example.com",
            Port = 587,
            Username = "user@example.com",
            Password = "secret"
        };

        var result = _validator.Validate(null, settings);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMissingHost_ReturnsFail()
    {
        var settings = new SmtpSettings { Host = "" };

        var result = _validator.Validate(null, settings);

        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Host is required");
    }

    [Fact]
    public void Validate_WithUsernameButNoPassword_ReturnsFail()
    {
        var settings = new SmtpSettings
        {
            Host = "smtp.example.com",
            Username = "user@example.com",
            Password = null  // Missing!
        };

        var result = _validator.Validate(null, settings);

        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Password is required");
    }
}
```
