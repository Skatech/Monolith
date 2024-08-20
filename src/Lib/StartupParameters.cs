using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

struct StartupParameters {
    private static readonly Regex _matchLong = new(
        @"\A--([a-zA-Z][a-zA-Z-]+)(=(.*))?\z", RegexOptions.Compiled);
    private static readonly Regex _matchShort = new(
        @"\A-([a-zA-Z]+)?(([a-zA-Z])=(.*))?\z", RegexOptions.Compiled);

    List<Parameter> _params;

    public static StartupParameters Create() => new StartupParameters { _params = new() };

    ///<summary>Add argument parameter, detected by order, value always required</summary>
    public StartupParameters AddArg(string? longName = null) {
        _params.Add(new Parameter(
            longName ?? _params.Count(p => p.IsParameter).ToString(), '\0', 0));
        RequireParameterLast().MarkParameter();
        return ValueRequired();
    }

    ///<summary>Add key parameter, detected by name, value can be unexpected, expected or required</summary>
    public StartupParameters AddKey(string longName, char shortName = '\0') {
        _params.Add(new Parameter(longName, shortName, 0));
        return this;
    }

    public StartupParameters UseRef(out Parameter reference) {
        reference = RequireParameterLast();
        return this;
    }

    public StartupParameters MismatchedWith(params Parameter[] args) {
        RequireParameterLast().MarkMismatched(args);
        return this;
    }

    public StartupParameters StrongOrdered(byte order) {
        RequireParameterLast().MarkStrongOrdered(order);
        return this;
    }

    public StartupParameters Required() {
        RequireParameterLast().MarkRequired();
        return this;
    }

    public StartupParameters ValueExpected() {
        RequireParameterLast().MarkValueExpected(false);
        return this;
    }

    public StartupParameters ValueRequired() {
        RequireParameterLast().MarkValueExpected(true);
        return this;
    }

    ///<summary>Process arguments and return error message, null when no errors</summary>
    public string? TryProcess(IEnumerable<string> args) {
        try {
            Process(args);
        }
        catch (Exception ex) {
             return ex.Message;
        }
        return null;
    }

    public void Process(IEnumerable<string> args) {
        byte order = 1;
        foreach (var arg in args) {
            var match = _matchLong.Match(arg);
            if (match.Success) {
                var param = RequireParameter(match.Groups[1].Value);
                if (match.Groups[2].Success) {
                    param.MarkActive(order++, match.Groups[3].Value);
                }
                else param.MarkActive(order++);
            }
            else if ((match = _matchShort.Match(arg)).Success) {
                if (match.Groups[1].Success) {
                    foreach(var c in match.Groups[1].ValueSpan) {
                        var param = RequireParameter(null, c);
                        param.MarkActive(order++);
                    }
                }
                if (match.Groups[2].Success) {
                    var param = RequireParameter(null, match.Groups[3].ValueSpan[0]);
                    param.MarkActive(order++, match.Groups[4].Value);
                }
            }
            else {
                if (_params.FirstOrDefault(p => p.IsParameter && p.IsNotActive) is Parameter param) {
                    param.MarkActive(order++, arg);
                }
                else throw new Exception($"Unexpected argument '{arg}'");
            }
        }
        foreach (var param in _params) {
            param.CheckConsistence();
        }
    }

    private Parameter RequireParameter(string? longName, char shortName = '\0', bool isParameter = false) {
        return _params.FirstOrDefault(longName is null
                ? p => isParameter == p.IsParameter && shortName.Equals(p.ShortName)
                : p => isParameter == p.IsParameter && longName.Equals(p.LongName))
            ?? throw new Exception(
                $"Unknown {(isParameter ? "parameter" : "key")} '{(longName ?? shortName.ToString())}'");
    }

    private Parameter RequireParameterLast() {
        return _params.LastOrDefault()
            ?? throw new Exception("No parameters added");
    }

    public class Parameter {
        private Flags _flags;
        private byte _order;
        private Parameter[]? _mismatch;
        public readonly string LongName;
        public readonly char ShortName;

        public string? ValueOrNull { get; private set; }

        public string Value => ValueOrNull
            ?? throw new Exception($"Empty parameter value access violation '{LongName}'");
        public bool HasValue => ValueOrNull is not null;
        public bool IsActive => _flags.HasFlag(Flags.Active);
        public bool IsNotActive => IsActive is false;
        public bool IsParameter => _flags.HasFlag(Flags.Parameter);

        public Parameter(string longName, char shortName, byte strongOrder = 0) {
            _order = strongOrder; _flags = Flags.None; LongName = longName; ShortName = shortName;
        }

        public void MarkParameter() {
            _flags |= Flags.Parameter;
        }

        public void MarkRequired() {
            _flags |= Flags.Required;
        }

        public void MarkValueExpected(bool valueRequired) {
            _flags |= valueRequired
                ? Flags.ValueExpected | Flags.ValueRequired
                : Flags.ValueExpected;
        }

        public void MarkStrongOrdered(byte order) {
            Debug.Assert((_order = order) > 0, "Strong order must be greater than zero");
        }

        public void MarkMismatched(Parameter[] mismatch) {
            _mismatch = mismatch;
        }

        public void MarkActive(byte order, string? value = null) {
            if (_flags.HasFlag(Flags.Active)) {
                throw new Exception($"Parameter duplication '{LongName}'");
            }
            if (_order > 0 && _order != order) {
                throw new Exception($"Parameter '{LongName}' order {order}, must be {_order}");
            }
            _flags |= Flags.Active;
            if (value is not null) {
                if (_flags.HasFlag(Flags.ValueExpected)) {
                    ValueOrNull = value;
                }
                else throw new Exception($"Parameter value unexpected '{LongName}'");
            }
            else if (_flags.HasFlag(Flags.ValueRequired)) {
                throw new Exception($"Parameter value required '{LongName}'");
            }
        }

        public void CheckConsistence() {
            if (_flags.HasFlag(Flags.Required) && IsNotActive)
                throw new Exception($"Requred parameter missing '{LongName}'");
            if (IsActive && _mismatch?.FirstOrDefault(p => p.IsActive) is Parameter mismatch) {
                throw new Exception($"Parameter '{mismatch.LongName}' incompatible with '{LongName}'");
            }
        }

        public override string ToString() {
            return ValueOrNull is not null
                ? $"'{LongName}' Active '{ValueOrNull}'"
                : IsActive
                    ? $"'{LongName}' Active"
                    : $"'{LongName}'";
        }

        // public static implicit operator bool(Parameter param) => param.Active;

        [Flags]
        private enum Flags : byte {
            None = 0, Required = 1,
            ValueExpected = 2, ValueRequired = 4,
            Parameter = 8, Active = 16
        }
    }
}