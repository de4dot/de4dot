/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CliSecure.vm {
	class Csvm {
		IDeobfuscatorContext deobfuscatorContext;
		ModuleDefinition module;
		EmbeddedResource resource;
		AssemblyNameReference vmAssemblyReference;

		public bool Detected {
			get { return resource != null && vmAssemblyReference != null; }
		}

		public EmbeddedResource Resource {
			get { return Detected ? resource : null; }
		}

		public AssemblyNameReference VmAssemblyReference {
			get { return Detected ? vmAssemblyReference : null; }
		}

		public Csvm(IDeobfuscatorContext deobfuscatorContext, ModuleDefinition module) {
			this.deobfuscatorContext = deobfuscatorContext;
			this.module = module;
		}

		public Csvm(IDeobfuscatorContext deobfuscatorContext, ModuleDefinition module, Csvm oldOne) {
			this.deobfuscatorContext = deobfuscatorContext;
			this.module = module;
			if (oldOne.resource != null)
				this.resource = (EmbeddedResource)module.Resources[oldOne.module.Resources.IndexOf(oldOne.resource)];
			if (oldOne.vmAssemblyReference != null)
				this.vmAssemblyReference = module.AssemblyReferences[oldOne.module.AssemblyReferences.IndexOf(oldOne.vmAssemblyReference)];
		}

		public void find() {
			resource = findCsvmResource();
			vmAssemblyReference = findVmAssemblyReference();
		}

		AssemblyNameReference findVmAssemblyReference() {
			foreach (var memberRef in module.GetMemberReferences()) {
				var method = memberRef as MethodReference;
				if (method == null)
					continue;
				if (method.FullName == "System.Object VMRuntime.Libraries.CSVMRuntime::RunMethod(System.String,System.Object[])")
					return method.DeclaringType.Scope as AssemblyNameReference;
			}
			return null;
		}

		EmbeddedResource findCsvmResource() {
			return DotNetUtils.getResource(module, "_CSVM") as EmbeddedResource;
		}

		public void restore() {
			if (!Detected)
				return;

			int oldIndent = Log.indentLevel;
			try {
				restore2();
			}
			finally {
				Log.indentLevel = oldIndent;
			}
		}

		void restore2() {
			Log.v("Restoring CSVM methods");
			Log.indent();

			var opcodeDetector = getVmOpCodeHandlerDetector();
			var csvmMethods = new CsvmDataReader(resource.GetResourceStream()).read();
			var converter = new CsvmToCilMethodConverter(deobfuscatorContext, module, opcodeDetector);
			var methodPrinter = new MethodPrinter();
			foreach (var csvmMethod in csvmMethods) {
				var cilMethod = module.LookupToken(csvmMethod.Token) as MethodDefinition;
				if (cilMethod == null)
					throw new ApplicationException(string.Format("Could not find method {0:X8}", csvmMethod.Token));
				converter.convert(cilMethod, csvmMethod);
				Log.v("Restored method {0:X8}", cilMethod.MetadataToken.ToInt32());
				printMethod(methodPrinter, cilMethod);
			}
			Log.deIndent();
		}

		static void printMethod(MethodPrinter methodPrinter, MethodDefinition method) {
			const Log.LogLevel dumpLogLevel = Log.LogLevel.verbose;
			if (!Log.isAtLeast(dumpLogLevel))
				return;

			Log.indent();

			Log.v("Locals:");
			Log.indent();
			for (int i = 0; i < method.Body.Variables.Count; i++)
				Log.v("#{0}: {1}", i, method.Body.Variables[i].VariableType);
			Log.deIndent();

			Log.v("Code:");
			Log.indent();
			methodPrinter.print(dumpLogLevel, method.Body.Instructions, method.Body.ExceptionHandlers);
			Log.deIndent();

			Log.deIndent();
		}

		VmOpCodeHandlerDetector getVmOpCodeHandlerDetector() {
			var vmFilename = vmAssemblyReference.Name + ".dll";
			var vmModulePath = Path.Combine(Path.GetDirectoryName(module.FullyQualifiedName), vmFilename);
			Log.v("CSVM filename: {0}", vmFilename);

			var dataKey = "cs cached VmOpCodeHandlerDetector";
			var dict = (Dictionary<string, VmOpCodeHandlerDetector>)deobfuscatorContext.getData(dataKey);
			if (dict == null)
				deobfuscatorContext.setData(dataKey, dict = new Dictionary<string, VmOpCodeHandlerDetector>(StringComparer.OrdinalIgnoreCase));
			VmOpCodeHandlerDetector detector;
			if (dict.TryGetValue(vmModulePath, out detector))
				return detector;
			dict[vmModulePath] = detector = new VmOpCodeHandlerDetector(ModuleDefinition.ReadModule(vmModulePath));

			detector.findHandlers();
			Log.v("CSVM opcodes:");
			Log.indent();
			for (int i = 0; i < detector.Handlers.Count; i++)
				Log.v("{0:X4}: {1}", i, detector.Handlers[i].Name);
			Log.deIndent();

			return detector;
		}
	}
}
