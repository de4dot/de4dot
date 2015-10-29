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
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks {
	public class Blocks {
		MethodDef method;
		IList<Local> locals;
		MethodBlocks methodBlocks;

		public MethodBlocks MethodBlocks {
			get { return methodBlocks; }
		}

		public IList<Local> Locals {
			get { return locals; }
		}

		public MethodDef Method {
			get { return method; }
		}

		public Blocks(MethodDef method) {
			this.method = method;
			UpdateBlocks();
		}

		public void UpdateBlocks() {
			var body = method.Body;
			locals = body.Variables;
			methodBlocks = new InstructionListParser(body.Instructions, body.ExceptionHandlers).Parse();
		}

		IEnumerable<ScopeBlock> GetAllScopeBlocks(ScopeBlock scopeBlock) {
			var list = new List<ScopeBlock>();
			list.Add(scopeBlock);
			list.AddRange(scopeBlock.GetAllScopeBlocks());
			return list;
		}

		public int RemoveDeadBlocks() {
			return new DeadBlocksRemover(methodBlocks).Remove();
		}

		public void GetCode(out IList<Instruction> allInstructions, out IList<ExceptionHandler> allExceptionHandlers) {
			new CodeGenerator(methodBlocks).GetCode(out allInstructions, out allExceptionHandlers);
		}

		struct LocalVariableInfo {
			public Block block;
			public int index;
			public LocalVariableInfo(Block block, int index) {
				this.block = block;
				this.index = index;
			}
		}

		public int OptimizeLocals() {
			if (locals.Count == 0)
				return 0;

			var usedLocals = new Dictionary<Local, List<LocalVariableInfo>>();
			foreach (var block in methodBlocks.GetAllBlocks()) {
				for (int i = 0; i < block.Instructions.Count; i++) {
					var instr = block.Instructions[i];
					Local local;
					switch (instr.OpCode.Code) {
					case Code.Ldloc:
					case Code.Ldloc_S:
					case Code.Ldloc_0:
					case Code.Ldloc_1:
					case Code.Ldloc_2:
					case Code.Ldloc_3:
					case Code.Stloc:
					case Code.Stloc_S:
					case Code.Stloc_0:
					case Code.Stloc_1:
					case Code.Stloc_2:
					case Code.Stloc_3:
						local = Instr.GetLocalVar(locals, instr);
						break;

					case Code.Ldloca_S:
					case Code.Ldloca:
						local = (Local)instr.Operand;
						break;

					default:
						local = null;
						break;
					}
					if (local == null)
						continue;

					List<LocalVariableInfo> list;
					if (!usedLocals.TryGetValue(local, out list))
						usedLocals[local] = list = new List<LocalVariableInfo>();
					list.Add(new LocalVariableInfo(block, i));
					if (usedLocals.Count == locals.Count)
						return 0;
				}
			}

			int newIndex = -1;
			var newLocals = new List<Local>(usedLocals.Count);
			var newLocalsDict = new Dictionary<Local, bool>(usedLocals.Count);
			foreach (var local in usedLocals.Keys) {
				newIndex++;
				newLocals.Add(local);
				newLocalsDict[local] = true;
				foreach (var info in usedLocals[local])
					info.block.Instructions[info.index] = new Instr(OptimizeLocalInstr(info.block.Instructions[info.index], local, (uint)newIndex));
			}

			// We can't remove all locals. Locals that reference another assembly will
			// cause the CLR to load that assembly before the method is executed if it
			// hasn't been loaded yet. This is a side effect the program may depend on.
			// At least one program has this dependency and will crash if we remove the
			// unused local. This took a while to figure out...
			var keptAssemblies = new Dictionary<string, bool>(StringComparer.Ordinal);
			foreach (var local in locals) {
				if (newLocalsDict.ContainsKey(local))
					continue;
				var defAsm = local.Type.DefinitionAssembly;
				if (defAsm == null)
					continue;	// eg. fnptr
				if (defAsm == method.DeclaringType.Module.Assembly)
					continue;	// this assembly is always loaded
				if (defAsm.IsCorLib())
					continue;	// mscorlib is always loaded
				var asmName = defAsm.FullName;
				if (keptAssemblies.ContainsKey(asmName))
					continue;
				keptAssemblies[asmName] = true;
				newLocals.Add(local);
			}

			int numRemoved = locals.Count - newLocals.Count;
			locals.Clear();
			foreach (var local in newLocals)
				locals.Add(local);
			return numRemoved;
		}

		static Instruction OptimizeLocalInstr(Instr instr, Local local, uint newIndex) {
			switch (instr.OpCode.Code) {
			case Code.Ldloc:
			case Code.Ldloc_S:
			case Code.Ldloc_0:
			case Code.Ldloc_1:
			case Code.Ldloc_2:
			case Code.Ldloc_3:
				if (newIndex == 0)
					return OpCodes.Ldloc_0.ToInstruction();
				if (newIndex == 1)
					return OpCodes.Ldloc_1.ToInstruction();
				if (newIndex == 2)
					return OpCodes.Ldloc_2.ToInstruction();
				if (newIndex == 3)
					return OpCodes.Ldloc_3.ToInstruction();
				if (newIndex <= 0xFF)
					return OpCodes.Ldloc_S.ToInstruction(local);
				return OpCodes.Ldloc.ToInstruction(local);

			case Code.Stloc:
			case Code.Stloc_S:
			case Code.Stloc_0:
			case Code.Stloc_1:
			case Code.Stloc_2:
			case Code.Stloc_3:
				if (newIndex == 0)
					return OpCodes.Stloc_0.ToInstruction();
				if (newIndex == 1)
					return OpCodes.Stloc_1.ToInstruction();
				if (newIndex == 2)
					return OpCodes.Stloc_2.ToInstruction();
				if (newIndex == 3)
					return OpCodes.Stloc_3.ToInstruction();
				if (newIndex <= 0xFF)
					return OpCodes.Stloc_S.ToInstruction(local);
				return OpCodes.Stloc.ToInstruction(local);

			case Code.Ldloca_S:
			case Code.Ldloca:
				if (newIndex <= 0xFF)
					return OpCodes.Ldloca_S.ToInstruction(local);
				return OpCodes.Ldloca.ToInstruction(local);

			default:
				throw new ApplicationException("Invalid ld/st local instruction");
			}
		}

		public void RepartitionBlocks() {
			MergeNopBlocks();
			foreach (var scopeBlock in GetAllScopeBlocks(methodBlocks)) {
				try {
					scopeBlock.RepartitionBlocks();
				}
				catch (NullReferenceException) {
					//TODO: Send this message to the log
					Console.WriteLine("Null ref exception! Invalid metadata token in code? Method: {0:X8}: {1}", method.MDToken.Raw, method.FullName);
					return;
				}
			}
		}

		void MergeNopBlocks() {
			var allBlocks = methodBlocks.GetAllBlocks();

			var nopBlocks = new Dictionary<Block, bool>();
			foreach (var nopBlock in allBlocks) {
				if (nopBlock.IsNopBlock())
					nopBlocks[nopBlock] = true;
			}

			if (nopBlocks.Count == 0)
				return;

			for (int i = 0; i < 10; i++) {
				bool modified = false;

				foreach (var block in allBlocks) {
					Block nopBlockTarget;

					nopBlockTarget = GetNopBlockTarget(nopBlocks, block, block.FallThrough);
					if (nopBlockTarget != null) {
						block.SetNewFallThrough(nopBlockTarget);
						modified = true;
					}

					if (block.Targets != null) {
						for (int targetIndex = 0; targetIndex < block.Targets.Count; targetIndex++) {
							nopBlockTarget = GetNopBlockTarget(nopBlocks, block, block.Targets[targetIndex]);
							if (nopBlockTarget == null)
								continue;
							block.SetNewTarget(targetIndex, nopBlockTarget);
							modified = true;
						}
					}
				}

				if (!modified)
					break;
			}

			foreach (var nopBlock in nopBlocks.Keys)
				nopBlock.Parent.RemoveDeadBlock(nopBlock);
		}

		static Block GetNopBlockTarget(Dictionary<Block, bool> nopBlocks, Block source, Block nopBlock) {
			if (nopBlock == null || !nopBlocks.ContainsKey(nopBlock) || source == nopBlock.FallThrough)
				return null;
			if (nopBlock.Parent.BaseBlocks[0] == nopBlock)
				return null;
			var target = nopBlock.FallThrough;
			if (nopBlock.Parent != target.Parent)
				return null;
			return target;
		}
	}
}
