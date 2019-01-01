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

namespace de4dot.code.deobfuscators {
	public class MethodCallRestorerBase {
		protected MemberRefBuilder builder;
		protected ModuleDefMD module;
		MethodDefAndDeclaringTypeDict<NewMethodInfo> oldToNewMethod = new MethodDefAndDeclaringTypeDict<NewMethodInfo>();

		class NewMethodInfo {
			public OpCode opCode;
			public IMethod method;

			public NewMethodInfo(OpCode opCode, IMethod method) {
				this.opCode = opCode;
				this.method = method;
			}
		}

		public MethodCallRestorerBase(ModuleDefMD module) {
			this.module = module;
			builder = new MemberRefBuilder(module);
		}

		public void CreateGetManifestResourceStream1(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			var assemblyType = builder.Type("System.Reflection", "Assembly", builder.CorLib);
			var streamType = builder.Type("System.IO", "Stream", builder.CorLib);
			var newMethod = builder.InstanceMethod("GetManifestResourceStream", assemblyType.TypeDefOrRef, streamType, builder.String);
			Add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		public void CreateGetManifestResourceStream2(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			var assemblyType = builder.Type("System.Reflection", "Assembly", builder.CorLib);
			var typeType = builder.Type("System", "Type", builder.CorLib);
			var streamType = builder.Type("System.IO", "Stream", builder.CorLib);
			var newMethod = builder.InstanceMethod("GetManifestResourceStream", assemblyType.TypeDefOrRef, streamType, typeType, builder.String);
			Add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		public void CreateGetManifestResourceNames(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			var assemblyType = builder.Type("System.Reflection", "Assembly", builder.CorLib);
			var stringArrayType = builder.Array(builder.String);
			var newMethod = builder.InstanceMethod("GetManifestResourceNames", assemblyType.TypeDefOrRef, stringArrayType);
			Add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		public void CreateGetReferencedAssemblies(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			var assemblyType = builder.Type("System.Reflection", "Assembly", builder.CorLib);
			var asmNameArray = builder.Array(builder.Type("System.Reflection", "AssemblyName", builder.CorLib));
			var newMethod = builder.InstanceMethod("GetReferencedAssemblies", assemblyType.TypeDefOrRef, asmNameArray);
			Add(oldMethod, newMethod, OpCodes.Callvirt);
		}

		public void CreateBitmapCtor(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			var bitmapType = builder.Type("System.Drawing", "Bitmap", "System.Drawing");
			var typeType = builder.Type("System", "Type", builder.CorLib);
			var newMethod = builder.InstanceMethod(".ctor", bitmapType.TypeDefOrRef, builder.Void, typeType, builder.String);
			Add(oldMethod, newMethod, OpCodes.Newobj);
		}

		public void CreateIconCtor(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			var iconType = builder.Type("System.Drawing", "Icon", "System.Drawing");
			var typeType = builder.Type("System", "Type", builder.CorLib);
			var newMethod = builder.InstanceMethod(".ctor", iconType.TypeDefOrRef, builder.Void, typeType, builder.String);
			Add(oldMethod, newMethod, OpCodes.Newobj);
		}

		protected void Add(MethodDef oldMethod, IMethod newMethod) => Add(oldMethod, newMethod, OpCodes.Callvirt);

		protected void Add(MethodDef oldMethod, IMethod newMethod, OpCode opCode) {
			if (oldMethod == null)
				return;
			oldToNewMethod.Add(oldMethod, new NewMethodInfo(opCode, newMethod));
		}

		public void Deobfuscate(Blocks blocks) {
			if (oldToNewMethod.Count == 0)
				return;
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var call = instrs[i];
					if (call.OpCode.Code != Code.Call)
						continue;
					var calledMethod = call.Operand as MethodDef;
					if (calledMethod == null)
						continue;

					var newMethodInfo = oldToNewMethod.Find(calledMethod);
					if (newMethodInfo == null)
						continue;

					instrs[i] = new Instr(Instruction.Create(newMethodInfo.opCode, newMethodInfo.method));
				}
			}
		}
	}
}
