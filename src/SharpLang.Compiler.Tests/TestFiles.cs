﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpLang.Compiler.Utils;

namespace SharpLang.CompilerServices.Tests
{
    public class TestFiles
    {
        [ThreadStatic]
        public static CodeDomProvider codeDomProvider;

        [Test, TestCaseSource("TestCases")]
        public static void Test(string sourceFile)
        {
            CompilerParameters compilerParameters;
            var outputAssembly = CompileAssembly(sourceFile);

            var bitcodeFile = Path.ChangeExtension(outputAssembly, "bc");

            SetupLocalToolchain();

            // Compile with a few additional corlib types useful for normal execution
            Driver.CompileAssembly(outputAssembly, bitcodeFile, verifyModule: true, additionalTypes: new[]
            {
                typeof(Exception),
                typeof(OverflowException),
                typeof(InvalidCastException),
                typeof(NotSupportedException),
                typeof(Array),
                typeof(String),
                typeof(AppDomain),
            });

            var outputFile = Path.Combine(Path.GetDirectoryName(outputAssembly),
                Path.GetFileNameWithoutExtension(outputAssembly) + "-llvm.exe");

            // Compile MiniCorlib.c
            Driver.ExecuteClang(string.Format("{0} -emit-llvm -c -o {1}", Path.Combine(Utils.GetTestsDirectory(), "MiniCorlib.c"), Path.Combine(Utils.GetTestsDirectory(), "MiniCorlib.bc")));

            // Link bitcode and runtime
            Driver.LinkBitcodes(outputFile, bitcodeFile, Path.Combine(Utils.GetTestsDirectory(), "MiniCorlib.bc"));

            // Execute original and ours
            var output1 = ExecuteAndCaptureOutput(outputAssembly);
            var output2 = ExecuteAndCaptureOutput(outputFile);

            // Compare output
            Assert.That(output2, Is.EqualTo(output1));
        }

        private static string CompileAssembly(string sourceFile)
        {
            var outputBase = Path.Combine(Path.GetDirectoryName(sourceFile), "output");
            Directory.CreateDirectory(outputBase);
            var outputAssembly = Path.Combine(outputBase, Path.GetFileNameWithoutExtension(sourceFile) + ".exe");

            if (Path.GetExtension(sourceFile) == ".cs")
                return CompileCSharpAssembly(sourceFile, outputAssembly);

            if (Path.GetExtension(sourceFile) == ".il")
                return CompileIlAssembly(sourceFile, outputAssembly);

            throw new NotImplementedException("Unknown source format.");
        }

        private static string CompileIlAssembly(string sourceFile, string outputAssembly)
        {
            // Locate ilasm
            var ilasmPath = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), "ilasm.exe");

            var ilasmArguments = string.Format("\"{0}\" /exe /out=\"{1}\"", sourceFile, outputAssembly);

            var processStartInfo = new ProcessStartInfo(ilasmPath, ilasmArguments);

            string ilasmOutput;
            var ilasmProcess = Utils.ExecuteAndCaptureOutput(processStartInfo, out ilasmOutput);
            ilasmProcess.WaitForExit();

            if (ilasmProcess.ExitCode != 0)
                throw new InvalidOperationException(string.Format("Error executing ilasm: {0}", ilasmOutput));

            return outputAssembly;
        }

        private static string CompileCSharpAssembly(string sourceFile, string outputAssembly)
        {
            if (codeDomProvider == null)
                codeDomProvider = new Microsoft.CSharp.CSharpCodeProvider();

            var compilerParameters = new CompilerParameters
            {
                IncludeDebugInformation = false,
                GenerateInMemory = false,
                GenerateExecutable = true,
                TreatWarningsAsErrors = false,
                CompilerOptions = "/unsafe /debug+ /debug:full",
                OutputAssembly = outputAssembly,
            };

            var compilerResults = codeDomProvider.CompileAssemblyFromFile(
                compilerParameters, sourceFile);

            if (compilerResults.Errors.HasErrors)
            {
                var errors = new StringBuilder();
                errors.AppendLine(string.Format("Serialization assembly compilation: {0} error(s)",
                    compilerResults.Errors.Count));
                foreach (var error in compilerResults.Errors)
                    errors.AppendLine(error.ToString());

                throw new InvalidOperationException(errors.ToString());
            }

            return outputAssembly;
        }

        private static void SetupLocalToolchain()
        {
            // Try to use locally compiled llc and clang (if they exist)
            var llcLocal = @"../../../../deps/llvm/build/RelWithDebInfo/bin/llc".Replace('/', Path.DirectorySeparatorChar);
            if (File.Exists(llcLocal) || File.Exists(llcLocal + ".exe"))
                Driver.LLC = llcLocal;

            var clangLocal = @"../../../../deps/llvm/build/RelWithDebInfo/bin/clang".Replace('/', Path.DirectorySeparatorChar);
            if (File.Exists(clangLocal) || File.Exists(clangLocal + ".exe"))
                Driver.Clang = clangLocal;

            // Probe again for local Ninja builds
            llcLocal = @"../../../../deps/llvm/build/bin/llc".Replace('/', Path.DirectorySeparatorChar);
            if (File.Exists(llcLocal) || File.Exists(llcLocal + ".exe"))
                Driver.LLC = llcLocal;

            clangLocal = @"../../../../deps/llvm/build/bin/clang".Replace('/', Path.DirectorySeparatorChar);
            if (File.Exists(clangLocal) || File.Exists(clangLocal + ".exe"))
                Driver.Clang = clangLocal;
        }

        /// <summary>
        /// Executes process and capture its output.
        /// </summary>
        /// <param name="executableFile">The executable file.</param>
        /// <returns></returns>
        private static string ExecuteAndCaptureOutput(string executableFile)
        {
            var processStartInfo = new ProcessStartInfo(executableFile);

            var output = new StringBuilder();
            processStartInfo.UseShellExecute = false;
            processStartInfo.CreateNoWindow = true;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;

            var process = new Process {StartInfo = processStartInfo};

            process.OutputDataReceived += (sender, args) =>
            {
                lock (output)
                {
                    if (args.Data != null)
                        output.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                lock (output)
                {
                    if (args.Data != null)
                        output.AppendLine(args.Data);
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException("Invalid exit code.");

            return output.ToString();
        }

        public static IEnumerable<string> TestCases
        {
            get
            {
                // Enumerate both .cs and .il files
                var directory = Utils.GetTestsDirectory();
                var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories);
                files = files.Concat(Directory.EnumerateFiles(directory, "*.il", SearchOption.AllDirectories));
                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }
    }
}