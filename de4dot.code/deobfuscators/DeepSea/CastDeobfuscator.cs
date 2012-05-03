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

using System;
using System.Collections.Generic;
using de4dot.blocks;
using de4dot.blocks.cflow;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.code.deobfuscators.DeepSea {
	class CastDeobfuscator : IBlocksDeobfuscator {
		Blocks blocks;
		Dictionary<VariableDefinition, LocalInfo> localInfos = new Dictionary<VariableDefinition, LocalInfo>();

		class LocalInfo {
			public readonly VariableDefinition local;
			TypeReference type;
			bool isValid;

			public TypeReference CastType {
				get { return type; }
				set {
					if (!isValid)
						return;

					if (value == null) {
						invalid();
						return;
					}

					if (type != null && !MemberReferenceHelper.compareTypes(type, value)) {
						invalid();
						return;
					}

					type = value;
				}
			}

			public LocalInfo(VariableDefinition local) {
				this.local = local;
				this.isValid = true;
			}

			public void invalid() {
				isValid = false;
				type = null;
			}

			public override string ToString() {
				if (type == null)
					return string.Format("{0} - INVALID", local);
				return string.Format("{0} - {1:X8} {2}", local, type.MetadataToken.ToInt32(), type.FullName);
			}
		}

		public bool ExecuteOnNoChange {
			get { return true; }
		}

		public void deobfuscateBegin(Blocks blocks) {
			this.blocks = blocks;
		}

		public bool deobfuscate(List<Block> allBlocks) {
			if (!init(allBlocks))
				return false;

			bool changed = false;

			var indexesToRemove = new List<int>();
			foreach (var block in allBlocks) {
				indexesToRemove.Clear();
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code == Code.Ldloca || instr.OpCode.Code == Code.Ldloca_S) {
						var local = instr.Operand as VariableDefinition;
						if (local == null)
							continue;
						localInfos[local].invalid();
					}
					else if (instr.isLdloc()) {
						var local = DotNetUtils.getLocalVar(blocks.Locals, instr.Instruction);
						if (local == null)
							continue;
						var localInfo = localInfos[local];
						var cast = instrs[i + 1];
						if (localInfo.CastType == null)
							continue;
						if (!isCast(cast))
							throw new ApplicationException("Not a cast instr");

						indexesToRemove.Add(i + 1);
					}
				}
				if (indexesToRemove.Count > 0) {
					block.remove(indexesToRemove);
					changed = true;
				}
			}

			foreach (var info in localInfos.Values) {
				if (info.CastType == null)
					continue;
				info.local.VariableType = info.CastType;
			}

			if (changed) {
				foreach (var block in allBlocks) {
					var instrs = block.Instructions;
					for (int i = 0; i < instrs.Count - 1; i++) {
						var instr = instrs[i];
						int castIndex = i + 1;
						if (instr.OpCode.Code == Code.Dup) {
							if (i == 0)
								continue;
							castIndex = i;
							instr = instrs[i - 1];
						}

						if (instr.isLdarg())
							addCast(block, castIndex, i + 1, DotNetUtils.getArgType(blocks.Method, instr.Instruction));
						else if (instr.OpCode.Code == Code.Ldfld || instr.OpCode.Code == Code.Ldsfld) {
							var field = instr.Operand as FieldReference;
							if (field == null)
								continue;
							addCast(block, castIndex, i + 1, field.FieldType);
						}
						else if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
							var calledMethod = instr.Operand as MethodReference;
							if (calledMethod == null || !DotNetUtils.hasReturnValue(calledMethod))
								continue;
							addCast(block, castIndex, i + 1, calledMethod.MethodReturnType.ReturnType);
						}
					}
				}
			}

			return changed;
		}

		bool addCast(Block block, int castIndex, int index, TypeReference type) {
			if (type == null)
				return false;
			if (castIndex >= block.Instructions.Count || index >= block.Instructions.Count)
				return false;
			var stloc = block.Instructions[index];
			if (!stloc.isStloc())
				return false;
			var local = DotNetUtils.getLocalVar(blocks.Locals, stloc.Instruction);
			if (local == null)
				return false;
			var localInfo = localInfos[local];
			if (localInfo.CastType == null)
				return false;

			if (!MemberReferenceHelper.compareTypes(localInfo.CastType, type))
				block.insert(castIndex, new Instruction(OpCodes.Castclass, localInfo.CastType));
			return true;
		}

		bool init(List<Block> allBlocks) {
			localInfos.Clear();
			foreach (var local in blocks.Locals)
				localInfos[local] = new LocalInfo(local);
			if (localInfos.Count == 0)
				return false;

			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var ldloc = instrs[i];
					if (!ldloc.isLdloc())
						continue;
					var local = DotNetUtils.getLocalVar(blocks.Locals, ldloc.Instruction);
					if (local == null)
						continue;
					var localInfo = localInfos[local];
					localInfo.CastType = getCastType(instrs[i + 1]);
				}
			}
			return true;
		}

		static bool isCast(Instr instr) {
			return instr.OpCode.Code == Code.Castclass || instr.OpCode.Code == Code.Isinst;
		}

		static TypeReference getCastType(Instr instr) {
			if (!isCast(instr))
				return null;
			return instr.Operand as TypeReference;
		}
	}
}
