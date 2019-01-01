/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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

using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	class ApplicationModeDecrypter {
		ModuleDefMD module;
		AssemblyResolver assemblyResolver;
		MemoryPatcher memoryPatcher;

		public byte[] AssemblyKey => assemblyResolver.Key;
		public byte[] AssemblyIv => assemblyResolver.Iv;
		public MemoryPatcher MemoryPatcher => memoryPatcher;
		public bool Detected => assemblyResolver != null;

		public ApplicationModeDecrypter(ModuleDefMD module) {
			this.module = module;
			Find();
		}

		void Find() {
			var cflowDeobfuscator = new CflowDeobfuscator(new MethodCallInliner(true));

			foreach (var type in module.Types) {
				if (DotNetUtils.GetPInvokeMethod(type, "kernel32", "CloseHandle") == null)
					continue;

				var resolver = new AssemblyResolver(type, cflowDeobfuscator);
				if (!resolver.Detected)
					continue;
				var patcher = new MemoryPatcher(type, cflowDeobfuscator);
				if (!patcher.Detected)
					continue;

				assemblyResolver = resolver;
				memoryPatcher = patcher;
				return;
			}
		}
	}
}
