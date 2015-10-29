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

using System.Collections.Generic;
using dnlib.DotNet;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	class AssemblyResolver {
		DecryptMethod decryptMethod = new DecryptMethod();

		public byte[] Key {
			get { return decryptMethod.Key; }
		}

		public byte[] Iv {
			get { return decryptMethod.Iv; }
		}

		public bool Detected {
			get { return decryptMethod.Detected; }
		}

		public AssemblyResolver(TypeDef type, ICflowDeobfuscator cflowDeobfuscator) {
			Find(type, cflowDeobfuscator);
		}

		void Find(TypeDef type, ICflowDeobfuscator cflowDeobfuscator) {
			var additionalTypes = new List<string> {
				"System.IO.BinaryReader",
				"System.IO.FileStream",
				"System.Reflection.Assembly",
				"System.Reflection.Assembly[]",
				"System.String",
			};
			foreach (var method in type.Methods) {
				if (!DotNetUtils.IsMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
					continue;
				if (!DecryptMethod.CouldBeDecryptMethod(method, additionalTypes))
					continue;
				cflowDeobfuscator.Deobfuscate(method);
				if (!decryptMethod.GetKey(method))
					continue;

				return;
			}
		}
	}
}
