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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks.cflow {
	// Only deobfuscates a method once. A copy of the method (now deobfuscated) is returned.
	public class CachedCflowDeobfuscator {
		BlocksCflowDeobfuscator cflowDeobfuscator = new BlocksCflowDeobfuscator();
		Dictionary<MethodDefinition, MethodDefinition> deobfuscated = new Dictionary<MethodDefinition, MethodDefinition>();

		public CachedCflowDeobfuscator() {
		}

		public CachedCflowDeobfuscator(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscators) {
			add(blocksDeobfuscators);
		}

		public void add(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscators) {
			foreach (var bd in blocksDeobfuscators)
				cflowDeobfuscator.add(bd);
		}

		public void add(IBlocksDeobfuscator blocksDeobfuscator) {
			cflowDeobfuscator.add(blocksDeobfuscator);
		}

		public MethodDefinition deobfuscate(MethodDefinition method) {
			MethodDefinition deobfuscatedMethod;
			if (deobfuscated.TryGetValue(method, out deobfuscatedMethod))
				return deobfuscatedMethod;

			if (method.Body == null || method.Body.Instructions.Count == 0) {
				deobfuscated[method] = method;
				return method;
			}

			deobfuscatedMethod = DotNetUtils.clone(method);
			deobfuscated[method] = deobfuscatedMethod;

			var blocks = new Blocks(deobfuscatedMethod);
			deobfuscate(blocks);
			IList<Instruction> allInstructions;
			IList<ExceptionHandler> allExceptionHandlers;
			blocks.getCode(out allInstructions, out allExceptionHandlers);
			DotNetUtils.restoreBody(deobfuscatedMethod, allInstructions, allExceptionHandlers);

			return deobfuscatedMethod;
		}

		void deobfuscate(Blocks blocks) {
			cflowDeobfuscator.init(blocks);
			cflowDeobfuscator.deobfuscate();
		}
	}
}
