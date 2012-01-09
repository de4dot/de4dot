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

using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	// Find the class that returns a RuntimeTypeHandle/RuntimeFieldHandle. The value passed to
	// its methods is the original metadata token, which will be different when we save the file.
	class MetadataTokenObfuscator {
		ModuleDefinition module;
		TypeDefinition type;
		MethodDefinition typeMethod;
		MethodDefinition fieldMethod;

		public TypeDefinition Type {
			get { return type; }
		}

		public MetadataTokenObfuscator(ModuleDefinition module) {
			this.module = module;
			find();
		}

		void find() {
			foreach (var type in module.Types) {
				var fields = type.Fields;
				if (fields.Count != 1)
					continue;
				if (fields[0].FieldType.FullName != "System.ModuleHandle")
					continue;
				if (type.HasProperties || type.HasEvents)
					continue;

				MethodDefinition fieldMethod = null, typeMethod = null;
				foreach (var method in type.Methods) {
					if (method.Parameters.Count != 1)
						continue;
					if (method.Parameters[0].ParameterType.FullName != "System.Int32")
						continue;
					if (method.MethodReturnType.ReturnType.FullName == "System.RuntimeTypeHandle")
						typeMethod = method;
					else if (method.MethodReturnType.ReturnType.FullName == "System.RuntimeFieldHandle")
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

		public void deobfuscate(Blocks blocks) {
			if (type == null)
				return;

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Ldc_I4)
						continue;
					var call = instrs[i + 1];
					if (call.OpCode.Code != Code.Call)
						continue;
					var method = call.Operand as MethodReference;
					if (method == null)
						continue;
					if (!MemberReferenceHelper.compareTypes(type, method.DeclaringType))
						continue;
					var methodDef = DotNetUtils.getMethod(module, method);
					if (methodDef == null)
						continue;
					if (methodDef != typeMethod && methodDef != fieldMethod)
						continue;

					int token = (int)instrs[i].Operand;
					instrs[i] = new Instr(Instruction.Create(OpCodes.Nop));
					instrs[i + 1] = new Instr(new Instruction(OpCodes.Ldtoken, module.LookupToken(token) as MemberReference));
				}
			}
		}
	}
}
