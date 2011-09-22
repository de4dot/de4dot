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
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.SmartAssembly {
	class TamperProtectionRemover {
		ModuleDefinition module;
		List<MethodDefinition> pinvokeMethods = new List<MethodDefinition>();

		public IList<MethodDefinition> PinvokeMethods {
			get { return pinvokeMethods; }
		}

		public TamperProtectionRemover(ModuleDefinition module) {
			this.module = module;
		}

		public bool remove(Blocks blocks) {
			if (blocks.Method.Name != ".cctor")
				return false;
			return removeTamperProtection(blocks);
		}

		bool isTamperProtected(IEnumerable<Block> allBlocks) {
			foreach (var block in allBlocks) {
				foreach (var instr in block.Instructions) {
					if (instr.OpCode != OpCodes.Ldstr)
						continue;
					var s = instr.Operand as string;
					if (s == "Assembly has been tampered")
						return true;
				}
			}
			return false;
		}

		class BlockInfo {
			public Block Block { get; set; }
			public int Start { get; set; }
			public int End { get; set; }
		}

		IList<BlockInfo> findTamperBlocks(Blocks blocks, IList<Block> allBlocks, out MethodDefinition pinvokeMethod) {
			var list = new List<BlockInfo>(3);

			var first = findFirstBlocks(allBlocks, blocks.Locals, out pinvokeMethod);
			if (first == null)
				return null;

			var second = first[1];
			var badBlock = second.Block.LastInstr.isBrfalse() ? second.Block.Targets[0] : second.Block.FallThrough;
			var last = findLastBlock(badBlock);
			if (last == null)
				return null;

			list.AddRange(first);
			list.Add(last);
			return list;
		}

		IList<BlockInfo> findFirstBlocks(IList<Block> allBlocks, IList<VariableDefinition> locals, out MethodDefinition pinvokeMethod) {
			pinvokeMethod = null;

			foreach (var b in allBlocks) {
				if (!b.LastInstr.isBrfalse())
					continue;

				try {
					var block = b;
					var list = new List<BlockInfo>();
					var instrs = block.Instructions;
					int start = instrs.Count - 1;
					int end = start;
					Instr instr;
					MethodReference method;

					/*
					 * ldc.i4.0
					 * stloc X
					 * call GetExecutingAssembly()
					 * stloc Y
					 * ldloc Y
					 * callvirt Location
					 * ldc.i4.1
					 * ldloca X
					 * call StrongNameSignatureVerificationEx
					 * pop
					 * ldloc X
					 * brfalse bad_code
					 * ldloc Y
					 * callvirt FullName()
					 * ldstr "......"
					 * callvirt EndsWith(string)
					 * brfalse bad_code / brtrue good_code
					 */

					instr = instrs[--start];
					if (!instr.isLdloc())
						continue;
					var loc0 = Instr.getLocalVar(locals, instr);

					instr = instrs[--start];
					if (instr.OpCode != OpCodes.Pop)
						continue;

					instr = instrs[--start];
					if (instr.OpCode != OpCodes.Call)
						continue;
					pinvokeMethod = DotNetUtils.getMethod(module, instr.Operand as MethodReference);
					if (!DotNetUtils.isPinvokeMethod(pinvokeMethod, "mscorwks", "StrongNameSignatureVerificationEx"))
						continue;

					while (true) {
						instr = instrs[--start];
						if (instr.OpCode == OpCodes.Callvirt)
							break;
					}
					method = (MethodReference)instr.Operand;
					if (method.ToString() != "System.String System.Reflection.Assembly::get_Location()")
						continue;

					while (true) {
						instr = instrs[--start];
						if (instr.OpCode == OpCodes.Call)
							break;
					}
					method = (MethodReference)instr.Operand;
					if (method.ToString() != "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()")
						continue;

					instr = instrs[--start];
					if (!instr.isStloc() || Instr.getLocalVar(locals, instr) != loc0)
						continue;
					instr = instrs[--start];
					if (!instr.isLdcI4())
						continue;

					list.Add(new BlockInfo {
						Block = block,
						Start = start,
						End = end,
					});

					block = block.FallThrough;
					instrs = block.Instructions;
					start = end = 0;

					instr = instrs[end++];
					if (!instr.isLdloc())
						continue;

					instr = instrs[end++];
					if (instr.OpCode != OpCodes.Callvirt)
						continue;
					method = (MethodReference)instr.Operand;
					if (method.ToString() != "System.String System.Reflection.Assembly::get_FullName()")
						continue;

					instr = instrs[end++];
					if (instr.OpCode != OpCodes.Ldstr)
						continue;

					instr = instrs[end++];
					if (instr.OpCode != OpCodes.Callvirt)
						continue;
					method = (MethodReference)instr.Operand;
					if (method.ToString() != "System.Boolean System.String::EndsWith(System.String)")
						continue;

					instr = instrs[end++];
					if (!instr.isBrfalse() && !instr.isBrtrue())
						continue;

					end--;
					list.Add(new BlockInfo {
						Block = block,
						Start = start,
						End = end,
					});

					return list;
				}
				catch (ArgumentOutOfRangeException) {
					continue;
				}
			}

			return null;
		}

		BlockInfo findLastBlock(Block last) {
			/*
			 * ldstr "........."
			 * newobj	System.Security.SecurityException(string)
			 * throw
			 */

			var instrs = last.Instructions;
			if (instrs.Count != 3)
				return null;

			Instr instr;
			int start = 0;
			int end = 0;

			instr = instrs[end++];
			if (instr.OpCode != OpCodes.Ldstr)
				return null;

			instr = instrs[end++];
			if (instr.OpCode != OpCodes.Newobj)
				return null;
			var method = (MethodReference)instr.Operand;
			if (method.ToString() != "System.Void System.Security.SecurityException::.ctor(System.String)")
				return null;

			instr = instrs[end++];
			if (instr.OpCode != OpCodes.Throw)
				return null;

			end--;
			return new BlockInfo {
				Block = last,
				Start = start,
				End = end,
			};
		}

		bool removeTamperProtection(Blocks blocks) {
			MethodDefinition pinvokeMethod;
			var allBlocks = new List<Block>(blocks.MethodBlocks.getAllBlocks());
			var blockInfos = findTamperBlocks(blocks, allBlocks, out pinvokeMethod);

			if (blockInfos == null) {
				if (isTamperProtected(allBlocks))
					Log.w("Could not remove tamper protection code: {0} ({1:X8})", blocks.Method, blocks.Method.MetadataToken.ToUInt32());
				return false;
			}

			if (blockInfos.Count != 3)
				throw new ApplicationException("Invalid state");
			var first = blockInfos[0];
			var second = blockInfos[1];
			var bad = blockInfos[2];
			if (first.Block.Targets.Count != 1 || first.Block.Targets[0] != bad.Block)
				throw new ApplicationException("Invalid state");
			if (second.Start != 0 || second.End + 1 != second.Block.Instructions.Count)
				throw new ApplicationException("Invalid state");
			if (bad.Start != 0 || bad.End + 1 != bad.Block.Instructions.Count)
				throw new ApplicationException("Invalid state");
			var goodBlock = second.Block.LastInstr.isBrtrue() ? second.Block.Targets[0] : second.Block.FallThrough;

			first.Block.remove(first.Start, first.End - first.Start + 1);
			first.Block.replaceLastInstrsWithBranch(0, goodBlock);
			removeDeadBlock(second);
			removeDeadBlock(bad);
			pinvokeMethods.Add(pinvokeMethod);

			return true;
		}

		void removeDeadBlock(BlockInfo info) {
			var parent = (ScopeBlock)info.Block.Parent;
			if (parent != null)	// null if already dead
				parent.removeDeadBlock(info.Block);
		}
	}
}
