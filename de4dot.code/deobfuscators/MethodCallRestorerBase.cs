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
	class MethodCallRestorerBase {
		protected MemberReferenceBuilder builder;
		protected ModuleDefinition module;
		MethodDefinitionAndDeclaringTypeDict<NewMethodInfo> oldToNewMethod = new MethodDefinitionAndDeclaringTypeDict<NewMethodInfo>();

		class NewMethodInfo {
			public OpCode opCode;
			public MethodReference method;

			public NewMethodInfo(OpCode opCode, MethodReference method) {
				this.opCode = opCode;
				this.method = method;
			}
		}

		public MethodCallRestorerBase(ModuleDefinition module) {
			this.module = module;
			this.builder = new MemberReferenceBuilder(module);
		}

		public void createGetManifestResourceStream1(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			var assemblyType = builder.type("System.Reflection", "Assembly", builder.CorLib);
			var streamType = builder.type("System.IO", "Stream", builder.CorLib);
			var newMethod = builder.instanceMethod("GetManifestResourceStream", assemblyType, streamType, builder.String);
			add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		public void createGetManifestResourceStream2(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			var assemblyType = builder.type("System.Reflection", "Assembly", builder.CorLib);
			var typeType = builder.type("System", "Type", builder.CorLib);
			var streamType = builder.type("System.IO", "Stream", builder.CorLib);
			var newMethod = builder.instanceMethod("GetManifestResourceStream", assemblyType, streamType, typeType, builder.String);
			add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		public void createGetManifestResourceNames(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			var assemblyType = builder.type("System.Reflection", "Assembly", builder.CorLib);
			var stringArrayType = builder.array(builder.String);
			var newMethod = builder.instanceMethod("GetManifestResourceNames", assemblyType, stringArrayType);
			add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		public void createBitmapCtor(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			var bitmapType = builder.type("System.Drawing", "Bitmap", "System.Drawing");
			var typeType = builder.type("System", "Type", builder.CorLib);
			var newMethod = builder.instanceMethod(".ctor", bitmapType, builder.Void, typeType, builder.String);
			add(oldMethod, newMethod, OpCodes.Newobj);
		}

		public void createIconCtor(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			var iconType = builder.type("System.Drawing", "Icon", "System.Drawing");
			var typeType = builder.type("System", "Type", builder.CorLib);
			var newMethod = builder.instanceMethod(".ctor", iconType, builder.Void, typeType, builder.String);
			add(oldMethod, newMethod, OpCodes.Newobj);
		}

		protected void add(MethodDefinition oldMethod, MethodReference newMethod) {
			add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		protected void add(MethodDefinition oldMethod, MethodReference newMethod, OpCode opCode) {
			if (oldMethod == null)
				return;
			oldToNewMethod.add(oldMethod, new NewMethodInfo(opCode, newMethod));
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

					var newMethodInfo = oldToNewMethod.find(calledMethod);
					if (newMethodInfo == null)
						continue;

					instrs[i] = new Instr(Instruction.Create(newMethodInfo.opCode, newMethodInfo.method));
				}
			}
		}
	}
}
