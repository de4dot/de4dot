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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	class AntiStrongName {
		public bool Remove(Blocks blocks) {
			var allBlocks = blocks.MethodBlocks.GetAllBlocks();
			foreach (var block in allBlocks) {
				if (Remove(blocks, block))
					return true;
			}

			return false;
		}

		bool Remove(Blocks blocks, Block block) {
			var instrs = block.Instructions;
			const int numInstrsToRemove = 11;
			if (instrs.Count < numInstrsToRemove)
				return false;
			int startIndex = instrs.Count - numInstrsToRemove;
			int index = startIndex;

			if (instrs[index++].OpCode.Code != Code.Ldtoken)
				return false;
			if (!CheckCall(instrs[index++], "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)"))
				return false;
			if (!CheckCall(instrs[index++], "System.Reflection.Assembly System.Type::get_Assembly()"))
				return false;
			if (!CheckCall(instrs[index++], "System.Reflection.AssemblyName System.Reflection.Assembly::GetName()"))
				return false;
			if (!CheckCall(instrs[index++], "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()"))
				return false;
			if (!CheckCall(instrs[index++], "System.String System.Convert::ToBase64String(System.Byte[])"))
				return false;
			if (instrs[index++].OpCode.Code != Code.Ldstr)
				return false;
			if (!CheckCall(instrs[index++], "System.String", "(System.String,System.String)"))
				return false;
			if (instrs[index++].OpCode.Code != Code.Ldstr)
				return false;
			if (!CheckCall(instrs[index++], "System.Boolean System.String::op_Inequality(System.String,System.String)"))
				return false;
			if (!instrs[index++].IsBrfalse())
				return false;

			var badBlock = block.FallThrough;
			var goodblock = block.Targets[0];
			if (badBlock == null)
				return false;

			if (badBlock == goodblock) {
				// All of the bad block was removed by the cflow deobfuscator. It was just a useless
				// calculation (div by zero).
				block.ReplaceLastInstrsWithBranch(numInstrsToRemove, goodblock);
			}
			else if (badBlock.Sources.Count == 1) {
				instrs = badBlock.Instructions;
				if (instrs.Count != 12)
					return false;
				index = 0;
				if (!instrs[index++].IsLdcI4())
					return false;
				if (!instrs[index].IsStloc())
					return false;
				var local = Instr.GetLocalVar(blocks.Locals, instrs[index++]);
				if (local == null)
					return false;
				if (!CheckLdloc(blocks.Locals, instrs[index++], local))
					return false;
				if (!CheckLdloc(blocks.Locals, instrs[index++], local))
					return false;
				if (instrs[index++].OpCode.Code != Code.Sub)
					return false;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					return false;
				if (!CheckStloc(blocks.Locals, instrs[index++], local))
					return false;
				if (!CheckLdloc(blocks.Locals, instrs[index++], local))
					return false;
				if (!CheckLdloc(blocks.Locals, instrs[index++], local))
					return false;
				if (instrs[index++].OpCode.Code != Code.Div)
					return false;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					return false;
				if (!CheckStloc(blocks.Locals, instrs[index++], local))
					return false;

				block.ReplaceLastInstrsWithBranch(numInstrsToRemove, goodblock);
				badBlock.Parent.RemoveDeadBlock(badBlock);
			}
			else
				return false;

			return true;
		}

		static bool CheckCall(Instr instr, string methodFullname) {
			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			var calledMethod = instr.Operand as IMethod;
			if (calledMethod == null)
				return false;
			return calledMethod.ToString() == methodFullname;
		}

		static bool CheckCall(Instr instr, string returnType, string parameters) {
			if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
				return false;
			var calledMethod = instr.Operand as IMethod;
			if (calledMethod == null)
				return false;
			return DotNetUtils.IsMethod(calledMethod, returnType, parameters);
		}

		static bool CheckLdloc(IList<Local> locals, Instr instr, Local local) {
			if (!instr.IsLdloc())
				return false;
			if (Instr.GetLocalVar(locals, instr) != local)
				return false;
			return true;
		}

		static bool CheckStloc(IList<Local> locals, Instr instr, Local local) {
			if (!instr.IsStloc())
				return false;
			if (Instr.GetLocalVar(locals, instr) != local)
				return false;
			return true;
		}
	}
}
