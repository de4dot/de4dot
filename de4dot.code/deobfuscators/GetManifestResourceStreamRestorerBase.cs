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

namespace de4dot.code.deobfuscators {
	class GetManifestResourceStreamRestorerBase {
		protected ModuleDefinition module;
		protected MethodDefinition getManifestResourceStream1Method;
		protected MethodDefinition getManifestResourceStream2Method;
		protected MethodDefinition getManifestResourceNamesMethod;
		protected MethodReference Assembly_GetManifestResourceStream1;
		protected MethodReference Assembly_GetManifestResourceStream2;
		protected MethodReference Assembly_GetManifestResourceNames;

		public MethodDefinition GetStream1Method {
			set { getManifestResourceStream1Method = value; }
		}

		public MethodDefinition GetStream2Method {
			set { getManifestResourceStream2Method = value; }
		}

		public MethodDefinition GetNamesMethod {
			set { getManifestResourceNamesMethod = value; }
		}

		public GetManifestResourceStreamRestorerBase(ModuleDefinition module) {
			this.module = module;
			createGetManifestResourceStreamMethods();
		}

		void createGetManifestResourceStreamMethods() {
			var assemblyType = new TypeReference("System.Reflection", "Assembly", module, module.TypeSystem.Corlib);
			var typeType = new TypeReference("System", "Type", module, module.TypeSystem.Corlib);
			var streamType = new TypeReference("System.IO", "Stream", module, module.TypeSystem.Corlib);
			var stringArrayType = new ArrayType(module.TypeSystem.String);

			Assembly_GetManifestResourceStream1 = new MethodReference("GetManifestResourceStream", streamType, assemblyType);
			Assembly_GetManifestResourceStream1.HasThis = true;
			Assembly_GetManifestResourceStream1.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

			Assembly_GetManifestResourceStream2 = new MethodReference("GetManifestResourceStream", streamType, assemblyType);
			Assembly_GetManifestResourceStream2.HasThis = true;
			Assembly_GetManifestResourceStream2.Parameters.Add(new ParameterDefinition(typeType));
			Assembly_GetManifestResourceStream2.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

			Assembly_GetManifestResourceNames = new MethodReference("GetManifestResourceNames", stringArrayType, assemblyType);
			Assembly_GetManifestResourceNames.HasThis = true;
		}

		public void deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var call = instrs[i];
					if (call.OpCode.Code != Code.Call)
						continue;
					var calledMethod = call.Operand as MethodDefinition;
					if (calledMethod == null)
						continue;

					MethodReference newMethod = null;
					if (calledMethod == getManifestResourceStream1Method)
						newMethod = Assembly_GetManifestResourceStream1;
					else if (calledMethod == getManifestResourceStream2Method)
						newMethod = Assembly_GetManifestResourceStream2;
					else if (calledMethod == getManifestResourceNamesMethod)
						newMethod = Assembly_GetManifestResourceNames;
					if (newMethod == null)
						continue;

					instrs[i] = new Instr(Instruction.Create(OpCodes.Callvirt, newMethod));
				}
			}
		}
	}
}
