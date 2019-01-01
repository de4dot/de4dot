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
	// Only deobfuscates a method once. A copy of the method (now deobfuscated) is returned.
	public class CachedCflowDeobfuscator {
		BlocksCflowDeobfuscator cflowDeobfuscator = new BlocksCflowDeobfuscator();
		Dictionary<MethodDef, MethodDef> deobfuscated = new Dictionary<MethodDef, MethodDef>();

		public CachedCflowDeobfuscator() {
		}

		public CachedCflowDeobfuscator(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscators) => Add(blocksDeobfuscators);

		public void Add(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscators) {
			foreach (var bd in blocksDeobfuscators)
				cflowDeobfuscator.Add(bd);
		}

		public void Add(IBlocksDeobfuscator blocksDeobfuscator) => cflowDeobfuscator.Add(blocksDeobfuscator);

		public MethodDef Deobfuscate(MethodDef method) {
			if (deobfuscated.TryGetValue(method, out var deobfuscatedMethod))
				return deobfuscatedMethod;

			if (method.Body == null || method.Body.Instructions.Count == 0) {
				deobfuscated[method] = method;
				return method;
			}

			deobfuscatedMethod = DotNetUtils.Clone(method);
			deobfuscated[method] = deobfuscatedMethod;

			var blocks = new Blocks(deobfuscatedMethod);
			Deobfuscate(blocks);
			blocks.GetCode(out var allInstructions, out var allExceptionHandlers);
			DotNetUtils.RestoreBody(deobfuscatedMethod, allInstructions, allExceptionHandlers);

			return deobfuscatedMethod;
		}

		void Deobfuscate(Blocks blocks) {
			cflowDeobfuscator.Initialize(blocks);
			cflowDeobfuscator.Deobfuscate();
		}
	}
}
