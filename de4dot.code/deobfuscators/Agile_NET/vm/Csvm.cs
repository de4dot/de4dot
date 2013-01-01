/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm {
	class Csvm {
		IDeobfuscatorContext deobfuscatorContext;
		ModuleDefMD module;
		EmbeddedResource resource;
		AssemblyRef vmAssemblyRef;

		public bool Detected {
			get { return resource != null && vmAssemblyRef != null; }
		}

		public EmbeddedResource Resource {
			get { return Detected ? resource : null; }
		}

		public Csvm(IDeobfuscatorContext deobfuscatorContext, ModuleDefMD module) {
			this.deobfuscatorContext = deobfuscatorContext;
			this.module = module;
		}

		public Csvm(IDeobfuscatorContext deobfuscatorContext, ModuleDefMD module, Csvm oldOne) {
			this.deobfuscatorContext = deobfuscatorContext;
			this.module = module;
			if (oldOne.resource != null)
				this.resource = (EmbeddedResource)module.Resources[oldOne.module.Resources.IndexOf(oldOne.resource)];
			if (oldOne.vmAssemblyRef != null)
				this.vmAssemblyRef = module.ResolveAssemblyRef(oldOne.vmAssemblyRef.Rid);
		}

		public void find() {
			resource = findCsvmResource();
			vmAssemblyRef = findVmAssemblyRef();
		}

		AssemblyRef findVmAssemblyRef() {
			foreach (var memberRef in module.GetMemberRefs()) {
				var sig = memberRef.MethodSig;
				if (sig == null)
					continue;
				if (sig.RetType.GetElementType() != ElementType.Object)
					continue;
				if (sig.Params.Count != 2)
					continue;
				if (memberRef.Name != "RunMethod")
					continue;
				if (memberRef.FullName == "System.Object VMRuntime.Libraries.CSVMRuntime::RunMethod(System.String,System.Object[])")
					return memberRef.DeclaringType.Scope as AssemblyRef;
			}
			return null;
		}

		EmbeddedResource findCsvmResource() {
			return DotNetUtils.getResource(module, "_CSVM") as EmbeddedResource;
		}

		public bool restore() {
			if (!Detected)
				return true;

			int oldIndent = Logger.Instance.IndentLevel;
			try {
				restore2();
				return true;
			}
			catch {
				return false;
			}
			finally {
				Logger.Instance.IndentLevel = oldIndent;
			}
		}

		void restore2() {
			Logger.v("Restoring CSVM methods");
			Logger.Instance.indent();

			var opcodeDetector = getVmOpCodeHandlerDetector();
			var csvmMethods = new CsvmDataReader(resource.Data).read();
			var converter = new CsvmToCilMethodConverter(deobfuscatorContext, module, opcodeDetector);
			var methodPrinter = new MethodPrinter();
			foreach (var csvmMethod in csvmMethods) {
				var cilMethod = module.ResolveToken(csvmMethod.Token) as MethodDef;
				if (cilMethod == null)
					throw new ApplicationException(string.Format("Could not find method {0:X8}", csvmMethod.Token));
				converter.convert(cilMethod, csvmMethod);
				Logger.v("Restored method {0:X8}", cilMethod.MDToken.ToInt32());
				printMethod(methodPrinter, cilMethod);
			}
			Logger.Instance.deIndent();
		}

		static void printMethod(MethodPrinter methodPrinter, MethodDef method) {
			const LoggerEvent dumpLogLevel = LoggerEvent.Verbose;
			if (Logger.Instance.IgnoresEvent(dumpLogLevel))
				return;

			Logger.Instance.indent();

			Logger.v("Locals:");
			Logger.Instance.indent();
			for (int i = 0; i < method.Body.Variables.Count; i++)
				Logger.v("#{0}: {1}", i, method.Body.Variables[i].Type);
			Logger.Instance.deIndent();

			Logger.v("Code:");
			Logger.Instance.indent();
			methodPrinter.print(dumpLogLevel, method.Body.Instructions, method.Body.ExceptionHandlers);
			Logger.Instance.deIndent();

			Logger.Instance.deIndent();
		}

		VmOpCodeHandlerDetector getVmOpCodeHandlerDetector() {
			var vmFilename = vmAssemblyRef.Name + ".dll";
			var vmModulePath = Path.Combine(Path.GetDirectoryName(module.Location), vmFilename);
			Logger.v("CSVM filename: {0}", vmFilename);

			var dataKey = "cs cached VmOpCodeHandlerDetector";
			var dict = (Dictionary<string, VmOpCodeHandlerDetector>)deobfuscatorContext.getData(dataKey);
			if (dict == null)
				deobfuscatorContext.setData(dataKey, dict = new Dictionary<string, VmOpCodeHandlerDetector>(StringComparer.OrdinalIgnoreCase));
			VmOpCodeHandlerDetector detector;
			if (dict.TryGetValue(vmModulePath, out detector))
				return detector;
			dict[vmModulePath] = detector = new VmOpCodeHandlerDetector(ModuleDefMD.Load(vmModulePath));

			detector.findHandlers();
			Logger.v("CSVM opcodes:");
			Logger.Instance.indent();
			for (int i = 0; i < detector.Handlers.Count; i++)
				Logger.v("{0:X4}: {1}", i, detector.Handlers[i].Name);
			Logger.Instance.deIndent();

			return detector;
		}
	}
}
