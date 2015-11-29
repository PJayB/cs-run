using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace cs_run
{
    class Program
    {
        const string DefaultOutputAssemblyName = "cs-run-temporary-assembly";

        class CompileSession
        {
            private List<string> _references = new List<string>();

            public bool WarnOnPartialMatch = true;
            public bool WarningsAsErrors = true;
            public int WarningLevel = 4;
            
            public string Script = string.Empty;
            public string EntryClass = "Program";
            public string EntryMethod = "Main";

            public IEnumerable<string> References { get { return _references; } }

            public CompileSession()
            {
            }

            public CompilerResults Compile()
            {
                if (string.IsNullOrWhiteSpace(Script))
                    throw new Exception("Script is empty.");

                CSharpCodeProvider codeProvider = new CSharpCodeProvider();

                ICodeCompiler compiler = codeProvider.CreateCompiler();
                CompilerParameters parameters = new CompilerParameters();
                parameters.GenerateExecutable = false;
                parameters.GenerateInMemory = true;
                parameters.MainClass = $"{EntryClass}.{EntryMethod}";
                parameters.IncludeDebugInformation = false;
                parameters.WarningLevel = WarningLevel;
                parameters.TreatWarningsAsErrors = WarningsAsErrors;
                
                foreach (var referenceName in _references)
                {
                    parameters.ReferencedAssemblies.Add(referenceName);
                }

                return compiler.CompileAssemblyFromSource(parameters, Script);
            }

            public void AddLocalReferences()
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (Assembly.GetExecutingAssembly() != asm)
                    {
                        _references.Add(asm.Location);
                    }
                }
            }

            public class MatchedPartialReferenceException : Exception
            {
                public string PartialName { get; private set; }
                public string ResolvedName { get; private set; }

                public MatchedPartialReferenceException(string referenceName, string resolvedTo)
                {
                    PartialName = referenceName;
                    ResolvedName = resolvedTo;
                }
            }

            public void AddReference(string referenceName, bool mustFullyMatch = false)
            {
                if (string.IsNullOrEmpty(referenceName))
                    throw new Exception("Missing: reference name");

                // Validate the assembly exists
                try
                {
                    Assembly assembly = Assembly.Load(referenceName);
                    _references.Add(assembly.Location);
                }
                catch (Exception ex)
                {
                    if (mustFullyMatch)
                        throw ex;

                    Assembly assembly = Assembly.LoadWithPartialName(referenceName);
                    _references.Add(assembly.Location);

                    if (WarnOnPartialMatch)
                    {
                        throw new MatchedPartialReferenceException(referenceName, assembly.FullName);
                    }
                }
            }
        }

        static Type GetEntryClassInfo(Assembly assembly, string entryClass)
        {
            var types = assembly.GetTypes();
            foreach (var t in types)
            {
                if (t.Name == entryClass && t.IsClass)
                {
                    return t;
                }
            }

            throw new MissingMemberException(assembly.GetName().Name, entryClass);
        }

        static void ExecuteScript(Assembly assembly, string entryClass, string entryMethod, string[] args)
        {
            // Find the type info for the entryClass
            Type entryClassInfo = GetEntryClassInfo(assembly, entryClass);

            BindingFlags bflags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance;

            // Find the type info for the entryMethod
            MethodInfo entryMethodInfo = entryClassInfo.GetMethod(entryMethod, bflags);
            if (entryMethodInfo == null)
                throw new Exception($"Couldn't get entry point {entryClass}.{entryMethod}");

            // Instantiate a root object?
            object rootObject = null;
            if (!entryMethodInfo.IsStatic)
            {
                // Create an instance of the script's entry point class
                rootObject = assembly.CreateInstance(entryClass);
                if (rootObject == null)
                    throw new Exception("Couldn't instantiate class " + entryClass);
            }

            try
            {
                entryMethodInfo.Invoke(rootObject, new object[] { args });
            }
            catch (Exception ex)
            {
                throw new Exception("SCRIPT EXCEPTION: " + ex.Message);
            }
        }

        static void SetEntryPoint(string arg, CompileSession session)
        {
            arg = arg.Trim();

            // Split based on last .
             int dot = arg.LastIndexOf('.');
            if (dot == -1 || dot == 0 || dot == arg.Length - 1)
            {
                throw new Exception("Entry point must take the form 'Class.Method'.");
            }

            string entryMethod = arg.Substring(dot + 1).Trim();
            string entryClass = arg.Substring(0, dot).Trim();

            if (string.IsNullOrEmpty(entryMethod))
                throw new Exception("Entry point must take the form 'Class.Method'.");
            if (string.IsNullOrEmpty(entryClass))
                throw new Exception("Entry point must take the form 'Class.Method'.");

            session.EntryClass = entryClass;
            session.EntryMethod = entryMethod;
        }

        static void AddCompilerSwitch(string arg, CompileSession session)
        {
            // Split based on first :
            string value = string.Empty;
            int colonPos = arg.IndexOf(':');
            if (colonPos != -1)
            {
                value = arg.Substring(colonPos + 1).Trim();
                arg = arg.Substring(0, colonPos).Trim();
            }

            arg = arg.ToLower();

            // Switch
            if (arg == "//ref")
            {
                try
                { 
                   session.AddReference(value);
                }
                catch (CompileSession.MatchedPartialReferenceException ex)
                {
                    Console.WriteLine($"WARNING: Adding reference based on partial name '{ex.PartialName}': '{ex.ResolvedName}'");
                }
            }
            else if (arg == "//entrypoint")
            {
                SetEntryPoint(value, session);
            }
            else if (arg == "//nopartialmatchwarning")
            {
                session.WarnOnPartialMatch = false;
            }
            else if (arg == "//nowarningsaserrors")
            {
                session.WarningsAsErrors = true;
            }
            else if (arg == "//warninglevel")
            {
                if (!int.TryParse(value, out session.WarningLevel))
                {
                    throw new Exception("Warning level expects a valid integer.");
                }
            }
        }

        class ShowHelpException : Exception {}

        static void MainProtected(string[] args)
        {
            // Validate the input arguments are sane
            if (args.Length < 1 || (args.Length == 1 && args[0] == "/?"))
            {
                throw new ShowHelpException();
            }

            CompileSession compileSession = new CompileSession();

            // Process the local arguments
            int argumentIndex = 0;
            while (argumentIndex < args.Length && args[argumentIndex].StartsWith("//"))
            {
                AddCompilerSwitch(args[argumentIndex++], compileSession);
            }

            // Get the filename
            if (argumentIndex == args.Length)
                throw new ShowHelpException();

            string filename = args[argumentIndex++];

            // Get the script arguments
            List<string> scriptArguments = new List<string>();
            for (int i = argumentIndex; i < args.Length; ++i)
            {
                scriptArguments.Add(args[i]);
            }
            
            // Link the session to to local assemblies
            compileSession.AddLocalReferences();

#if DEBUG
            foreach (var i in compileSession.References)
                Console.WriteLine(i);
#endif

            // Open the input file (which is the script file)
            try
            {
                using (StreamReader reader = new StreamReader(filename))
                {
                    // Load the entire file into a string
                    compileSession.Script = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error reading script: " + ex.Message);
            }

            // Compile the assembly
            CompilerResults results = compileSession.Compile();
            if (results.Errors.Count > 0)
            {
                foreach (var error in results.Errors)
                {
                    Console.WriteLine(error.ToString());
                }

                if (results.Errors.HasErrors)
                    throw new Exception("Compilation failed with errors.");
            }

            // Execute the assembly
            ExecuteScript(results.CompiledAssembly, compileSession.EntryClass, compileSession.EntryMethod, scriptArguments.ToArray());
        }

        static void Main(string[] args)
        {
            try
            {
                MainProtected(args);
            }
            catch (ShowHelpException)
            {
                Console.WriteLine("Usage: cs-run [//NoWarningsAsErrors] [//WarningLevel:<val>] [//NoPartialMatchWarning] [//EntryPoint:<Class>.<Method>] [//Ref:<Reference>] <filename.cs> [Script arguments...]");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Environment.Exit(1);
                return;
            }
        }
    }
}
