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

using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	// Find the class that returns a RuntimeTypeHandle/RuntimeFieldHandle. The value passed to
	// its methods is the original metadata token, which will be different when we save the file.
	class MetadataTokenObfuscator {
		ModuleDefMD module;
		TypeDef type;
		MethodDef typeMethod;
		MethodDef fieldMethod;

		public TypeDef Type {
			get { return type; }
		}

		public MetadataTokenObfuscator(ModuleDefMD module) {
			this.module = module;
			Find();
		}

		void Find() {
			foreach (var type in module.Types) {
				var fields = type.Fields;
				if (fields.Count != 1)
					continue;
				if (fields[0].FieldType.FullName != "System.ModuleHandle")
					continue;
				if (type.HasProperties || type.HasEvents)
					continue;

				MethodDef fieldMethod = null, typeMethod = null;
				foreach (var method in type.Methods) {
					var sig = method.MethodSig;
					if (sig == null || sig.Params.Count != 1)
						continue;
					if (sig.Params[0].GetElementType() != ElementType.I4)
						continue;
					if (sig.RetType.GetFullName() == "System.RuntimeTypeHandle")
						typeMethod = method;
					else if (sig.RetType.GetFullName() == "System.RuntimeFieldHandle")
						fieldMethod = method;
				}

				if (typeMethod == null || fieldMethod == null)
					continue;

				this.type = type;
				this.typeMethod = typeMethod;
				this.fieldMethod = fieldMethod;
				return;
			}
		}

		public void Deobfuscate(Blocks blocks) {
			if (type == null)
				return;

			var gpContext = GenericParamContext.Create(blocks.Method);
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Ldc_I4)
						continue;
					var call = instrs[i + 1];
					if (call.OpCode.Code != Code.Call)
						continue;
					var method = call.Operand as IMethod;
					if (method == null)
						continue;
					if (!new SigComparer().Equals(type, method.DeclaringType))
						continue;
					var methodDef = DotNetUtils.GetMethod(module, method);
					if (methodDef == null)
						continue;
					if (methodDef != typeMethod && methodDef != fieldMethod)
						continue;

					uint token = (uint)(int)instrs[i].Operand;
					instrs[i] = new Instr(OpCodes.Nop.ToInstruction());
					instrs[i + 1] = new Instr(new Instruction(OpCodes.Ldtoken, module.ResolveToken(token, gpContext) as ITokenOperand));
				}
			}
		}
	}
}
