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

using System;
using System.Collections.Generic;
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	// Removes dead stores by replacing the stloc with a pop. Other optimizations will notice it's
	// dead code and remove it.
	// I've only seen Xenocode generate this kind of code, so the code below is a special case of
	// the more general case.
	class DeadStoreRemover : IBlocksDeobfuscator {
		Blocks blocks;
		List<Block> allBlocks;
		List<AccessFlags> localFlags = new List<AccessFlags>();
		List<bool> deadLocals = new List<bool>();

		[Flags]
		enum AccessFlags {
			None = 0,
			Read = 1,
			Write = 2,
		}

		public bool ExecuteIfNotModified { get; set; }

		public void DeobfuscateBegin(Blocks blocks) => this.blocks = blocks;

		public bool Deobfuscate(List<Block> allBlocks) {
			this.allBlocks = allBlocks;
			return Remove();
		}

		bool Remove() {
			if (blocks.Locals.Count == 0)
				return false;

			localFlags.Clear();
			deadLocals.Clear();
			for (int i = 0; i < blocks.Locals.Count; i++) {
				localFlags.Add(AccessFlags.None);
				deadLocals.Add(false);
			}

			FindLoadStores();

			bool deadStores = false;
			for (int i = 0; i < blocks.Locals.Count; i++) {
				var flags = localFlags[i];
				if ((flags & AccessFlags.Read) == AccessFlags.None) {
					deadLocals[i] = true;
					deadStores = true;
				}
			}
			if (!deadStores)
				return false;

			return RemoveDeadStores();
		}

		void FindLoadStores() {
			foreach (var block in allBlocks) {
				foreach (var instr in block.Instructions) {
					Local local;
					AccessFlags flags;
					switch (instr.OpCode.Code) {
					case Code.Ldloc:
					case Code.Ldloc_S:
					case Code.Ldloc_0:
					case Code.Ldloc_1:
					case Code.Ldloc_2:
					case Code.Ldloc_3:
						local = Instr.GetLocalVar(blocks.Locals, instr);
						flags = AccessFlags.Read;
						break;

					case Code.Stloc:
					case Code.Stloc_S:
					case Code.Stloc_0:
					case Code.Stloc_1:
					case Code.Stloc_2:
					case Code.Stloc_3:
						local = Instr.GetLocalVar(blocks.Locals, instr);
						flags = AccessFlags.Write;
						break;

					case Code.Ldloca_S:
					case Code.Ldloca:
						local = instr.Operand as Local;
						flags = AccessFlags.Read | AccessFlags.Write;
						break;

					default:
						local = null;
						flags = AccessFlags.None;
						break;
					}

					if (local == null)
						continue;
					localFlags[local.Index] |= flags;
				}
			}
		}

		bool RemoveDeadStores() {
			bool modified = false;
			foreach (var block in allBlocks) {
				var instructions = block.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var instr = instructions[i];
					Local local;
					switch (instr.OpCode.Code) {
					case Code.Stloc:
					case Code.Stloc_S:
					case Code.Stloc_0:
					case Code.Stloc_1:
					case Code.Stloc_2:
					case Code.Stloc_3:
						local = Instr.GetLocalVar(blocks.Locals, instr);
						break;

					default:
						continue;
					}

					if (local == null)
						continue;
					if (!deadLocals[local.Index])
						continue;
					instructions[i] = new Instr(OpCodes.Pop.ToInstruction());
					modified = true;
				}
			}

			return modified;
		}
	}
}
