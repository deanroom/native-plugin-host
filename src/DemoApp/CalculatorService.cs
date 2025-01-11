using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NativeAotPluginHost;
using System.Runtime.InteropServices;

namespace DemoApp;

public class CalculatorService : BackgroundService
{
    private readonly ILogger<CalculatorService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PluginHost _pluginHost;
    private AddDelegate? _add;
    private SubtractDelegate? _subtract;
    private Hello? _hello;
    private SetLoggerFactory? _setLoggerFactory;
    private SetLogger? _setLogger;
    private GCHandle _loggerFactoryHandle;
    private GCHandle _loggerHandle;

    // Define delegate types
    private delegate int AddDelegate(int a, int b);
    private delegate int SubtractDelegate(int a, int b);
    private delegate void Hello();
    private delegate void SetLoggerFactory(IntPtr loggerFactory);
    private delegate void SetLogger(IntPtr logger);
    public CalculatorService(ILogger<CalculatorService> logger, ILoggerFactory loggerFactory, PluginHost pluginHost)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _pluginHost = pluginHost;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            InitializePluginHost(stoppingToken);
            await ProcessUserCommands(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while running the calculator demo");
        }
    }

    private void InitializePluginHost(CancellationToken stoppingToken)
    {
        string runtimeConfigPath = Path.Combine(AppContext.BaseDirectory, "ManagedLibrary.runtimeconfig.json");
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "ManagedLibrary.dll");

        _logger.LogInformation("Loading assembly from: {AssemblyPath}", assemblyPath);
        _logger.LogInformation("Using config from: {ConfigPath}", runtimeConfigPath);

        _logger.LogInformation("Initializing runtime...");
        _pluginHost.Initialize(runtimeConfigPath);

        // Load Add method
        _logger.LogInformation("Loading Add method...");
        _add = _pluginHost.GetFunction<AddDelegate>(
            assemblyPath,
            "ManagedLibrary.Calculator, ManagedLibrary",
            "Add");

        // Load Subtract method
        _logger.LogInformation("Loading Subtract method...");
        _subtract = _pluginHost.GetFunction<SubtractDelegate>(
            assemblyPath,
            "ManagedLibrary.Calculator, ManagedLibrary",
            "Subtract");

        // Load Hello method
        _logger.LogInformation("Loading Hello method...");
        _hello = _pluginHost.GetFunction<Hello>(
            assemblyPath,
            "ManagedLibrary.Calculator, ManagedLibrary",
            "Hello");

        // Load SetLoggerFactory method
        _logger.LogInformation("Loading SetLoggerFactory method...");
        _setLoggerFactory = _pluginHost.GetFunction<SetLoggerFactory>(
            assemblyPath,
            "ManagedLibrary.Calculator, ManagedLibrary",
            "SetLoggerFactory");

        // Load SetLogger method
        _logger.LogInformation("Loading SetLogger method...");
        _setLogger = _pluginHost.GetFunction<SetLogger>(
            assemblyPath,
            "ManagedLibrary.Calculator, ManagedLibrary",
            "SetLogger");

        var calculatorLogger = _loggerFactory.CreateLogger("ManagedLibrary.Calculator");
        
        // Free existing handles if they are allocated
        if (_loggerFactoryHandle.IsAllocated)
            _loggerFactoryHandle.Free();
        if (_loggerHandle.IsAllocated)
            _loggerHandle.Free();

        // Create new handles
        _loggerFactoryHandle = GCHandle.Alloc(_loggerFactory);
        _loggerHandle = GCHandle.Alloc(calculatorLogger);

        _logger.LogInformation("Setting logger factory");
        _setLoggerFactory?.Invoke(GCHandle.ToIntPtr(_loggerFactoryHandle));

        _logger.LogInformation("Setting calculator logger");
        _setLogger?.Invoke(GCHandle.ToIntPtr(_loggerHandle));

        _logger.LogInformation("Calculator is ready. Available commands:");
        _logger.LogInformation("- add(x,y)");
        _logger.LogInformation("- sub(x,y)");
        _logger.LogInformation("- hello");
        _logger.LogInformation("Press Ctrl+C to exit");
    }

    private async Task ProcessUserCommands(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                await Console.Out.WriteLineAsync("Enter command :");
                var input = await Console.In.ReadLineAsync(stoppingToken);
                if (string.IsNullOrEmpty(input)) continue;

                var command = CommandParser.ParseCommand(input);
                if (command == null)
                {
                    _logger.LogWarning("Invalid command format. Available commands:");
                    _logger.LogWarning("- add(x,y)");
                    _logger.LogWarning("- subtract(x,y)");
                    _logger.LogWarning("- hello");
                    continue;
                }

                try
                {
                    var (operation, a, b) = command.Value;
                    switch (operation)
                    {
                        case "add" when a.HasValue && b.HasValue:
                            int addResult = _add?.Invoke(a.Value, b.Value) ??
                                throw new InvalidOperationException("Add function not loaded");
                            _logger.LogInformation("Result: {Result}", addResult);
                            break;

                        case "sub" when a.HasValue && b.HasValue:
                            int subtractResult = _subtract?.Invoke(a.Value, b.Value) ??
                                throw new InvalidOperationException("Subtract function not loaded");
                            _logger.LogInformation("Result: {Result}", subtractResult);
                            break;

                        case "hello":
                            if (_hello == null)
                                throw new InvalidOperationException("Hello function not loaded");
                            _hello.Invoke();
                            break;

                        default:
                            throw new InvalidOperationException($"Unknown operation: {operation}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing command");
                }
            }
            else
            {
                await Task.Delay(100, stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping calculator demo service");
        if (_loggerFactoryHandle.IsAllocated)
            _loggerFactoryHandle.Free();
        if (_loggerHandle.IsAllocated)
            _loggerHandle.Free();
        _pluginHost.Dispose();
        await base.StopAsync(cancellationToken);
    }
}