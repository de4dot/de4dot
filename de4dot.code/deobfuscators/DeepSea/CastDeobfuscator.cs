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
using de4dot.blocks;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.DeepSea {
	class CastDeobfuscator : IBlocksDeobfuscator {
		Blocks blocks;
		Dictionary<Local, LocalInfo> localInfos = new Dictionary<Local, LocalInfo>();

		class LocalInfo {
			public readonly Local local;
			ITypeDefOrRef type;
			bool isValid;

			public ITypeDefOrRef CastType {
				get => type;
				set {
					if (!isValid)
						return;

					if (value == null) {
						Invalid();
						return;
					}

					if (type != null && !new SigComparer().Equals(type, value)) {
						Invalid();
						return;
					}

					type = value;
				}
			}

			public LocalInfo(Local local) {
				this.local = local;
				isValid = true;
			}

			public void Invalid() {
				isValid = false;
				type = null;
			}

			public override string ToString() {
				if (type == null)
					return $"{local} - INVALID";
				return $"{local} - {type.MDToken.ToInt32():X8} {type.FullName}";
			}
		}

		public bool ExecuteIfNotModified => true;
		public void DeobfuscateBegin(Blocks blocks) => this.blocks = blocks;

		public bool Deobfuscate(List<Block> allBlocks) {
			if (!Initialize(allBlocks))
				return false;

			bool modified = false;

			var indexesToRemove = new List<int>();
			foreach (var block in allBlocks) {
				indexesToRemove.Clear();
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code == Code.Ldloca || instr.OpCode.Code == Code.Ldloca_S) {
						var local = instr.Operand as Local;
						if (local == null)
							continue;
						localInfos[local].Invalid();
					}
					else if (instr.IsLdloc()) {
						var local = instr.Instruction.GetLocal(blocks.Locals);
						if (local == null)
							continue;
						var localInfo = localInfos[local];
						var cast = instrs[i + 1];
						if (localInfo.CastType == null)
							continue;
						if (!IsCast(cast))
							throw new ApplicationException("Not a cast instr");

						indexesToRemove.Add(i + 1);
					}
				}
				if (indexesToRemove.Count > 0) {
					block.Remove(indexesToRemove);
					modified = true;
				}
			}

			foreach (var info in localInfos.Values) {
				if (info.CastType == null)
					continue;
				info.local.Type = info.CastType.ToTypeSig();
			}

			if (modified) {
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

						if (instr.IsLdarg())
							AddCast(block, castIndex, i + 1, instr.Instruction.GetArgumentType(blocks.Method.MethodSig, blocks.Method.DeclaringType));
						else if (instr.OpCode.Code == Code.Ldfld || instr.OpCode.Code == Code.Ldsfld) {
							var field = instr.Operand as IField;
							if (field == null)
								continue;
							AddCast(block, castIndex, i + 1, field.FieldSig.GetFieldType());
						}
						else if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
							var calledMethod = instr.Operand as IMethod;
							if (calledMethod == null || !DotNetUtils.HasReturnValue(calledMethod))
								continue;
							AddCast(block, castIndex, i + 1, calledMethod.MethodSig.GetRetType());
						}
					}
				}
			}

			return modified;
		}

		bool AddCast(Block block, int castIndex, int index, TypeSig type) {
			if (type == null)
				return false;
			if (castIndex >= block.Instructions.Count || index >= block.Instructions.Count)
				return false;
			var stloc = block.Instructions[index];
			if (!stloc.IsStloc())
				return false;
			var local = stloc.Instruction.GetLocal(blocks.Locals);
			if (local == null)
				return false;
			var localInfo = localInfos[local];
			if (localInfo.CastType == null)
				return false;

			if (!new SigComparer().Equals(localInfo.CastType, type))
				block.Insert(castIndex, new Instruction(OpCodes.Castclass, localInfo.CastType));
			return true;
		}

		bool Initialize(List<Block> allBlocks) {
			localInfos.Clear();
			foreach (var local in blocks.Locals)
				localInfos[local] = new LocalInfo(local);
			if (localInfos.Count == 0)
				return false;

			foreach (var block in allBlocks) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var ldloc = instrs[i];
					if (!ldloc.IsLdloc())
						continue;
					var local = ldloc.Instruction.GetLocal(blocks.Locals);
					if (local == null)
						continue;
					var localInfo = localInfos[local];
					localInfo.CastType = GetCastType(instrs[i + 1]);
				}
			}
			return true;
		}

		static bool IsCast(Instr instr) => instr.OpCode.Code == Code.Castclass || instr.OpCode.Code == Code.Isinst;

		static ITypeDefOrRef GetCastType(Instr instr) {
			if (!IsCast(instr))
				return null;
			return instr.Operand as ITypeDefOrRef;
		}
	}
}
