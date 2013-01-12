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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class TamperProtectionRemover {
		ModuleDefMD module;
		List<MethodDef> pinvokeMethods = new List<MethodDef>();

		enum Type {
			V1,
			V2,
		}

		public IList<MethodDef> PinvokeMethods {
			get { return pinvokeMethods; }
		}

		public TamperProtectionRemover(ModuleDefMD module) {
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

		class TamperBlocks {
			public Type type;
			public MethodDef pinvokeMethod;
			public BlockInfo first;
			public BlockInfo second;
			public BlockInfo bad;
		}

		TamperBlocks findTamperBlocks(Blocks blocks, IList<Block> allBlocks) {
			var tamperBlocks = new TamperBlocks();

			if (!findFirstBlocks(tamperBlocks, allBlocks, blocks.Locals))
				return null;

			var second = tamperBlocks.second;
			var badBlock = second.Block.LastInstr.isBrfalse() ? second.Block.Targets[0] : second.Block.FallThrough;
			tamperBlocks.bad = findBadBlock(badBlock);
			if (tamperBlocks.bad == null)
				return null;

			return tamperBlocks;
		}

		bool findFirstBlocks(TamperBlocks tamperBlocks, IList<Block> allBlocks, IList<Local> locals) {
			foreach (var b in allBlocks) {
				try {
					if (findFirstBlocks(b, tamperBlocks, allBlocks, locals))
						return true;
				}
				catch (ArgumentOutOfRangeException) {
					continue;
				}
			}

			return false;
		}

		static int findCallMethod(Block block, int index, bool keepLooking, Func<IMethod, bool> func) {
			var instrs = block.Instructions;
			for (int i = index; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
					continue;

				var calledMethod = instr.Operand as IMethod;
				if (calledMethod != null && func(calledMethod))
					return i;
				if (!keepLooking)
					return -1;
			}
			return -1;
		}

		bool findFirstBlocks(Block block, TamperBlocks tamperBlocks, IList<Block> allBlocks, IList<Local> locals) {
			if (!block.LastInstr.isBrfalse())
				return false;

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
			 * pop / brfalse bad_code
			 * ldloc X
			 * brfalse bad_code
			 * ldloc Y
			 * callvirt FullName()
			 * ldstr "......"
			 * callvirt EndsWith(string)
			 * brfalse bad_code / brtrue good_code
			 */

			var instrs = block.Instructions;
			int end = instrs.Count - 1;
			Instr instr;
			IMethod method;
			tamperBlocks.type = Type.V1;

			int index = 0;

			int start = findCallMethod(block, index, true, (calledMethod) => calledMethod.ToString() == "System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()");
			if (start < 0)
				return false;
			index = start + 1;
			instr = instrs[--start];
			if (!instr.isStloc())
				return false;
			var loc0 = Instr.getLocalVar(locals, instr);
			instr = instrs[--start];
			if (!instr.isLdcI4())
				return false;

			index = findCallMethod(block, index, false, (calledMethod) => calledMethod.ToString() == "System.String System.Reflection.Assembly::get_Location()");
			if (index < 0)
				return false;
			index++;

			index = findCallMethod(block, index, false, (calledMethod) => {
				tamperBlocks.pinvokeMethod = DotNetUtils.getMethod(module, calledMethod);
				return DotNetUtils.isPinvokeMethod(tamperBlocks.pinvokeMethod, "mscorwks", "StrongNameSignatureVerificationEx");
			});
			if (index < 0)
				return false;
			index++;

			if (!instrs[index].isBrfalse()) {
				if (instrs[index].OpCode.Code != Code.Pop)
					return false;
				instr = instrs[index + 1];
				if (!instr.isLdloc() || Instr.getLocalVar(locals, instr) != loc0)
					return false;
				if (!instrs[index + 2].isBrfalse())
					return false;

				tamperBlocks.type = Type.V1;
				tamperBlocks.first = new BlockInfo {
					Block = block,
					Start = start,
					End = end,
				};
			}
			else {
				tamperBlocks.type = Type.V2;
				tamperBlocks.first = new BlockInfo {
					Block = block,
					Start = start,
					End = end,
				};

				block = block.FallThrough;
				if (block == null)
					return false;
				instrs = block.Instructions;
				index = 0;
				instr = instrs[index];
				if (!instr.isLdloc() || Instr.getLocalVar(locals, instr) != loc0)
					return false;
				if (!instrs[index + 1].isBrfalse())
					return false;
			}

			block = block.FallThrough;
			instrs = block.Instructions;
			start = end = 0;

			instr = instrs[end++];
			if (!instr.isLdloc())
				return false;

			instr = instrs[end++];
			if (instr.OpCode != OpCodes.Callvirt)
				return false;
			method = instr.Operand as IMethod;
			if (method == null || method.ToString() != "System.String System.Reflection.Assembly::get_FullName()")
				return false;

			instr = instrs[end++];
			if (instr.OpCode != OpCodes.Ldstr)
				return false;

			instr = instrs[end++];
			if (instr.OpCode != OpCodes.Callvirt)
				return false;
			method = instr.Operand as IMethod;
			if (method == null || method.ToString() != "System.Boolean System.String::EndsWith(System.String)")
				return false;

			instr = instrs[end++];
			if (!instr.isBrfalse() && !instr.isBrtrue())
				return false;

			end--;
			tamperBlocks.second = new BlockInfo {
				Block = block,
				Start = start,
				End = end,
			};

			return true;
		}

		BlockInfo findBadBlock(Block last) {
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
			var method = instr.Operand as IMethod;
			if (method == null || method.ToString() != "System.Void System.Security.SecurityException::.ctor(System.String)")
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
			var allBlocks = blocks.MethodBlocks.getAllBlocks();
			var tamperBlocks = findTamperBlocks(blocks, allBlocks);

			if (tamperBlocks == null) {
				if (isTamperProtected(allBlocks))
					Logger.w("Could not remove tamper protection code: {0} ({1:X8})", Utils.removeNewlines(blocks.Method), blocks.Method.MDToken.ToUInt32());
				return false;
			}

			switch (tamperBlocks.type) {
			case Type.V1:
				removeTamperV1(tamperBlocks);
				break;
			case Type.V2:
				removeTamperV2(tamperBlocks);
				break;
			default:
				throw new ApplicationException("Unknown type");
			}
			pinvokeMethods.Add(tamperBlocks.pinvokeMethod);

			return true;
		}

		void removeTamperV1(TamperBlocks tamperBlocks) {
			var first = tamperBlocks.first;
			var second = tamperBlocks.second;
			var bad = tamperBlocks.bad;
			var goodBlock = second.Block.LastInstr.isBrtrue() ? second.Block.Targets[0] : second.Block.FallThrough;

			if (first.Block.Targets.Count != 1 || first.Block.Targets[0] != bad.Block)
				throw new ApplicationException("Invalid state");

			first.Block.remove(first.Start, first.End - first.Start + 1);
			first.Block.replaceLastInstrsWithBranch(0, goodBlock);
			removeDeadBlock(second.Block);
			removeDeadBlock(bad.Block);
		}

		void removeTamperV2(TamperBlocks tamperBlocks) {
			var first = tamperBlocks.first;
			var second = tamperBlocks.second.Block;
			var bad = tamperBlocks.bad.Block;
			var firstFallthrough = first.Block.FallThrough;
			var goodBlock = second.LastInstr.isBrtrue() ? second.Targets[0] : second.FallThrough;

			if (first.Block.Targets.Count != 1 || first.Block.Targets[0] != bad)
				throw new ApplicationException("Invalid state");

			first.Block.remove(first.Start, first.End - first.Start + 1);
			first.Block.replaceLastInstrsWithBranch(0, goodBlock);
			removeDeadBlock(firstFallthrough);
			removeDeadBlock(second);
			removeDeadBlock(bad);
		}

		void removeDeadBlock(Block block) {
			var parent = block.Parent;
			if (parent != null)	// null if already dead
				parent.removeDeadBlock(block);
		}
	}
}
