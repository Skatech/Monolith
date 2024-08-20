using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;

namespace Skatech.ComponentArchitecture;

public static class ReflectionExtensions {
    public static IEnumerable<Type> EnumerateComponents(
            this Assembly assembly, string nameSpace, string classNamePrefix) {
        return assembly.GetTypes()
            .Where(t => nameSpace.Equals(t.Namespace) && t.Name.StartsWith(classNamePrefix));
    }
    
    public static T GetStaticFieldValue<T>(this Type componentType, string fieldName) where T : class {
        var fieldInfo = componentType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        return fieldInfo?.GetValue(null) as T ??
            throw new InvalidOperationException(
                $"Unable to get value of staitc field: '{componentType.Name}.{fieldName}'.");
    }

    public static void InvokeStaticMethod(this Type componentType, string methodName, params object[] args) {
        var method = componentType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static) ?? 
            throw new InvalidOperationException(
                $"Unable to find staitc method: '{componentType.Name}.{methodName}'.");

        var x = args[0];
        method.Invoke(null, args);
    }
}