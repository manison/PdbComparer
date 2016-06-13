using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Serilog;

namespace PdbComparer
{
    class Program
    {

        class Options
        {
            [Option(HelpText = "First assembly (source).")]
            [ValueOption(0)]
            public string Source { get; set; }

            [Option(Required = true, HelpText = "Second assembly (rewritten).")]
            [ValueOption(1)]
            public string Actual { get; set; }

            [Option(HelpText = "LogLevel.", DefaultValue = Serilog.Events.LogEventLevel.Warning)]
            public Serilog.Events.LogEventLevel LogLevel { get; set; }

            [Option(HelpText = "Determine if line nr for first sequence points are compared, enabled by default")]
            public bool DisableLineNrComparison { get; set; }
        }

        const string Assembly_First = "First";
        const string Assembly_Second = "Second";
        static bool CompareLineNr { get; set; }

        static int Main(string[] args)
        {
            Options options = new Options();
            if (!Parser.Default.ParseArguments(args, options))
            {
                var helptext = CommandLine.Text.HelpText.AutoBuild(options);
                Console.Write(helptext.ToString());
                return -1;
            }

            Log.Logger = new LoggerConfiguration()
                .WriteTo.ColoredConsole(options.LogLevel)
                .WriteTo.File("PdbComparer.log", options.LogLevel)
                .CreateLogger();
            CompareLineNr = !options.DisableLineNrComparison;

            AssemblyDefinition assemblyDefinition = LoadAssemblyDefinition(options.Source);
            AssemblyDefinition otherAssemblyDefinition = LoadAssemblyDefinition(options.Actual);

            return CompareModuleInformation(assemblyDefinition.MainModule, otherAssemblyDefinition.MainModule);
        }

        private static int CompareModuleInformation(ModuleDefinition module, ModuleDefinition otherModule)
        {
            int errors = 0;

            foreach (var type in module.Types)
            {
                var otherType = otherModule.GetType(type.FullName);
                if (otherType == null)
                {
                    Log.Warning("Missing type {Type} in {Assembly}", type.FullName, Assembly_Second);
                    ++errors;
                }
                else
                {
                    errors += CompareTypeInformation(type, otherType);
                }
            }

            return errors;
        }

        private static int CompareTypeInformation(TypeDefinition type, TypeDefinition otherType)
        {
            var dict = otherType.Methods.ToDictionary(m => GetMethodIdentifier(m), StringComparer.Ordinal);
            int errors = 0;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body.Instructions.Count == 0)
                    continue;

                MethodDefinition otherMethod;
                var methodName = GetMethodIdentifier(method);
                if (!dict.TryGetValue(methodName, out otherMethod))
                {
                    Log.Error("{Method} not found in {Assembly}", method, Assembly_Second);
                    ++errors;
                    continue;
                }

                if (!otherMethod.HasBody)
                {
                    Log.Error("{Method} does not have a body in {Assembly}", method, Assembly_Second);
                    ++errors;
                    continue;
                }

                errors += CompareMethodInformation(method, otherMethod, methodName);
            }

            return errors;
        }

        private static int CompareMethodInformation(MethodDefinition method, MethodDefinition otherMethod, string methodName)
        {
            SequencePoint s1 = GetFirstSequencePoint(method);
            SequencePoint s2 = GetFirstSequencePoint(otherMethod);
            if (s1 == null && s2 == null)
            {
                Log.Information("Skipping {Method} - neither have sequence points", methodName);
                return 0;
            }
            else if (s1 == null && s2 != null)
            {
                Log.Warning("Missing sequence point in {Method} in {Assmebly}", methodName, Assembly_First);
                return 1;
            }
            else if (s1 != null && s2 == null)
            {
                Log.Warning("Missing sequence point in {Method} in {Assmebly}", methodName, Assembly_Second);
                return 1;
            }
            else // s1 != null && s2 != null
            {
                if (string.Compare(s1.Document.Url, s2.Document.Url, StringComparison.Ordinal) != 0)
                {
                    Log.Error("Different source file for {Method}: {Url1} vs {Url2}", methodName, s1.Document.Url, s2.Document.Url);
                    return 1;
                }
                else if (CompareLineNr && s1.StartLine != s2.StartLine)
                {
                    Log.Information(" OK? - Different start line for {Method}: {Line} vs {Line2}", methodName, s1.StartLine, s2.StartLine);
                    return 1;
                }
                else
                {
                    Log.Debug(" OK - {Method} same", methodName);

                    // Just ensure that all sequence points in the same method belong to the same document
                    return VerifyAllSequencePoints(otherMethod, methodName, Assembly_Second, s2.Document.Url);
                }
            }
        }

        private static SequencePoint GetFirstSequencePoint(MethodDefinition method)
        {
            SequencePoint s1 = null;
            for (int i = 0; s1 == null && i < method.Body.Instructions.Count; ++i)
                s1 = method.Body.Instructions[i].SequencePoint;
            return s1;
        }

        private static int VerifyAllSequencePoints(MethodDefinition m, string methodName, string assemblyName, string url)
        {
            int errors = 0;
            var instructions = m.Body.Instructions;

            for (int i = 0; i < instructions.Count; ++i)
            {
                var sp = instructions[i].SequencePoint;
                if (sp != null && string.CompareOrdinal(sp.Document.Url, url) != 0)
                {
                    Log.Error("A sequence point in {Method}:{Line} in {Assembly} had a different url than the first one ({Url1} vs {Url2})"
                        , methodName, sp.StartLine, assemblyName, sp.Document.Url, url);
                    ++errors;
                }
            }

            return errors;
        }

        private static string GetMethodIdentifier(MethodDefinition method)
        {
            return method.FullName;
        }

        private static AssemblyDefinition LoadAssemblyDefinition(string assemblyName)
        {
            ReaderParameters readerParameters = new ReaderParameters
            {
                ReadSymbols = true,
            };
            AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyName, readerParameters);
            return assemblyDefinition;
        }
    }
}
