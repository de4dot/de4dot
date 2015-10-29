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
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	public class CflowDeobfuscator : ICflowDeobfuscator {
		BlocksCflowDeobfuscator cflowDeobfuscator = new BlocksCflowDeobfuscator();

		public CflowDeobfuscator() {
		}

		public CflowDeobfuscator(IBlocksDeobfuscator blocksDeobfuscator) {
			cflowDeobfuscator.Add(blocksDeobfuscator);
		}

		public void Deobfuscate(MethodDef method) {
			Deobfuscate(method, (blocks) => {
				cflowDeobfuscator.Initialize(blocks);
				cflowDeobfuscator.Deobfuscate();
			});
		}

		static bool HasNonEmptyBody(MethodDef method) {
			return method.Body != null && method.Body.Instructions.Count > 0;
		}

		void Deobfuscate(MethodDef method, Action<Blocks> handler) {
			if (HasNonEmptyBody(method)) {
				var blocks = new Blocks(method);

				handler(blocks);

				IList<Instruction> allInstructions;
				IList<ExceptionHandler> allExceptionHandlers;
				blocks.GetCode(out allInstructions, out allExceptionHandlers);
				DotNetUtils.RestoreBody(method, allInstructions, allExceptionHandlers);
			}
		}
	}
}
