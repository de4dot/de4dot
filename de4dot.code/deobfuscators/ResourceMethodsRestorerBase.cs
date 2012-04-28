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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	class ResourceMethodsRestorerBase {
		protected MemberReferenceBuilder builder;
		protected ModuleDefinition module;

		MethodDefinitionAndDeclaringTypeDict<MethodReference> oldToNewMethod = new MethodDefinitionAndDeclaringTypeDict<MethodReference>();

		public ResourceMethodsRestorerBase(ModuleDefinition module) {
			this.module = module;
			this.builder = new MemberReferenceBuilder(module);
		}

		public void createGetManifestResourceStream1(MethodDefinition oldMethod) {
			var assemblyType = builder.type("System.Reflection", "Assembly", builder.CorLib);
			var streamType = builder.type("System.IO", "Stream", builder.CorLib);
			var newMethod = builder.instanceMethod("GetManifestResourceStream", assemblyType, streamType, builder.String);
			add(oldMethod, newMethod);
		}

		public void createGetManifestResourceStream2(MethodDefinition oldMethod) {
			var assemblyType = builder.type("System.Reflection", "Assembly", builder.CorLib);
			var typeType = builder.type("System", "Type", builder.CorLib);
			var streamType = builder.type("System.IO", "Stream", builder.CorLib);
			var newMethod = builder.instanceMethod("GetManifestResourceStream", assemblyType, streamType, typeType, builder.String);
			add(oldMethod, newMethod);
		}

		public void createGetManifestResourceNames(MethodDefinition oldMethod) {
			var assemblyType = builder.type("System.Reflection", "Assembly", builder.CorLib);
			var stringArrayType = builder.array(builder.String);
			var newMethod = builder.instanceMethod("GetManifestResourceNames", assemblyType, stringArrayType);
			add(oldMethod, newMethod);
		}

		void add(MethodDefinition oldMethod, MethodReference newMethod) {
			if (oldMethod == null)
				return;
			oldToNewMethod.add(oldMethod, newMethod);
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

					var newMethod = oldToNewMethod.find(calledMethod);
					if (newMethod == null)
						continue;

					instrs[i] = new Instr(Instruction.Create(OpCodes.Callvirt, newMethod));
				}
			}
		}
	}
}
