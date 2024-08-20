using System;
using System.Data;
using System.Linq;
using System.Reflection;
using Skatech.ComponentArchitecture;

const string APPDATA_ENVVAR = "MONOLITH_APPDATA";
const string UTILS_NAMESPACE = "Skatech.Monolith.Utilities", UTILS_CLASSPREFIX = "Utility";

try {
    if (Environment.GetEnvironmentVariable(APPDATA_ENVVAR) is string appdatadir) {
        OperationGroup.SetApplicationDataDirectory(appdatadir);    
        var operations = new OperationGroup(
            Assembly.GetExecutingAssembly(), UTILS_NAMESPACE, UTILS_CLASSPREFIX);
            
        if (args.Length < 1) {
            var applicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
            var commandDescriptions = String.Join("\r\n",
                operations.CreateDescriptionMap().Select(p => $"  {p.Key,-6} - {p.Value}"));            
            Console.WriteLine($"""
                Skatech Lab (c) 2024 - Monolith Utilities v{applicationVersion} - Commands:
                {commandDescriptions}
                """);
        }
        else if (operations.TryRunOperation(args[0], args[1..])) {
            // founded and executed successfully
        }
        else Console.WriteLine(
            $"Unknown command '{args[0]}'");
    }
    else Console.WriteLine($"{APPDATA_ENVVAR} is not set");
}
catch (Exception ex) {
    using var cc = ConsoleColors.FromForeground(ConsoleColor.Red);
    Console.WriteLine($"ERROR: {ex.InnerException?.Message ?? ex.Message}");
}
