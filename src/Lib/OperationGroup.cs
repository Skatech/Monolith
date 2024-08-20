using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Skatech.ComponentArchitecture;

/* Operation template:

    static class OperationFoo {
        public const string Name = "foo";
        public const string Description = "Foo operation";
        public static void Run(string[] args);
    }
*/
class OperationGroup {
    const string NAME_FIELD = "Name", DESC_FIELD = "Description", RUN_METHOD = "Run";
    readonly Dictionary<string, Type> _operations;
    
    public OperationGroup(Assembly assembly, string nameSpace, string classNamePrefix) {
        _operations = assembly.EnumerateComponents(nameSpace, classNamePrefix)
            .ToDictionary(t => t.GetStaticFieldValue<string>(NAME_FIELD));
    }

    public Dictionary<string, string> CreateDescriptionMap() {
        return _operations.ToDictionary(p => p.Key, p => p.Value.GetStaticFieldValue<string>(DESC_FIELD));
    }

    public bool TryRunOperation(string operationName, string[] args) {
        if (_operations.TryGetValue(operationName, out Type? component)) {
            component.InvokeStaticMethod(RUN_METHOD, new object[] {args});
            return true;
        }
        return false;
    }

    static string? _appdatadir;

    public static string ApplicationDataDirectory =>
        _appdatadir ?? throw new InvalidOperationException(
            $"Application data directory must be set before first access.");

    public static void SetApplicationDataDirectory(string path) {
        Directory.CreateDirectory(_appdatadir = Path.GetFullPath(path, Environment.CurrentDirectory));
    }
}