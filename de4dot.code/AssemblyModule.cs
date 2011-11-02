/*
    Copyright (C) 2011 de4dot@gmail.com

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
using System.IO;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;

namespace de4dot {
	class AssemblyModule {
		string filename;
		string methodsFilename;
		Dictionary<uint, DumpedMethod> dumpedMethods;
		ModuleDefinition module;

		public AssemblyModule(string filename, string methodsFilename = null) {
			this.filename = Utils.getFullPath(filename);
			this.methodsFilename = methodsFilename;

			if (this.methodsFilename == null)
				this.methodsFilename = this.filename + ".methods";
		}

		public ModuleDefinition load() {
			readMethodsFile();
			readFile();
			return module;
		}

		public void save(string newFilename) {
			module.Write(newFilename);
		}

		public ModuleDefinition reload(byte[] newModuleData, Dictionary<uint, DumpedMethod> dumpedMethods) {
			var assemblyResolver = AssemblyResolver.Instance;
			assemblyResolver.removeModule(module);
			DotNetUtils.typeCaches.invalidate(module);
			this.dumpedMethods = dumpedMethods;

			var readerParameters = new ReaderParameters(ReadingMode.Deferred);
			readerParameters.AssemblyResolver = assemblyResolver;
			module = ModuleDefinition.ReadModule(new MemoryStream(newModuleData), readerParameters, dumpedMethods);
			assemblyResolver.addModule(module);
			return module;
		}

		void readMethodsFile() {
			if (new FileInfo(methodsFilename).Exists) {
				using (var reader = new BinaryReader(File.Open(methodsFilename, FileMode.Open, FileAccess.Read, FileShare.Read))) {
					dumpedMethods = new DumpedMethodsReader(reader).read();
				}
			}
			else {
				dumpedMethods = new Dictionary<uint, DumpedMethod>();
			}
		}

		void readFile() {
			var assemblyResolver = AssemblyResolver.Instance;
			var readerParameters = new ReaderParameters(ReadingMode.Deferred);
			readerParameters.AssemblyResolver = assemblyResolver;
			module = ModuleDefinition.ReadModule(filename, readerParameters, dumpedMethods);
			assemblyResolver.addModule(module);
		}

		public override string ToString() {
			return filename;
		}
	}
}
