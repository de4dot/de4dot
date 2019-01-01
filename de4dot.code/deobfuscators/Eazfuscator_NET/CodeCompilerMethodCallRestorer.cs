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

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class CodeCompilerMethodCallRestorer : MethodCallRestorerBase {
		ITypeDefOrRef CodeDomProvider => builder.Type("System.CodeDom.Compiler", "CodeDomProvider", "System").ToTypeDefOrRef();
		ITypeDefOrRef ICodeCompiler => builder.Type("System.CodeDom.Compiler", "ICodeCompiler", "System").ToTypeDefOrRef();
		TypeSig CompilerResults => builder.Type("System.CodeDom.Compiler", "CompilerResults", "System");
		TypeSig CompilerParameters => builder.Type("System.CodeDom.Compiler", "CompilerParameters", "System");
		TypeSig CodeCompileUnit => builder.Type("System.CodeDom", "CodeCompileUnit", "System");
		TypeSig CodeCompileUnitArray => builder.Array(CodeCompileUnit);
		TypeSig StringArray => builder.Array(builder.String);

		public CodeCompilerMethodCallRestorer(ModuleDefMD module) : base(module) { }

		public void Add_CodeDomProvider_CompileAssemblyFromDom(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromDom", CodeDomProvider, CompilerResults, CompilerParameters, CodeCompileUnitArray));
		}

		public void Add_CodeDomProvider_CompileAssemblyFromFile(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromFile", CodeDomProvider, CompilerResults, CompilerParameters, StringArray));
		}

		public void Add_CodeDomProvider_CompileAssemblyFromSource(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromSource", CodeDomProvider, CompilerResults, CompilerParameters, StringArray));
		}

		public void Add_ICodeCompiler_CompileAssemblyFromDom(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromDom", ICodeCompiler, CompilerResults, CompilerParameters, CodeCompileUnit));
		}

		public void Add_ICodeCompiler_CompileAssemblyFromDomBatch(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromDomBatch", ICodeCompiler, CompilerResults, CompilerParameters, CodeCompileUnitArray));
		}

		public void Add_ICodeCompiler_CompileAssemblyFromFile(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromFile", ICodeCompiler, CompilerResults, CompilerParameters, builder.String));
		}

		public void Add_ICodeCompiler_CompileAssemblyFromFileBatch(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromFileBatch", ICodeCompiler, CompilerResults, CompilerParameters, StringArray));
		}

		public void Add_ICodeCompiler_CompileAssemblyFromSource(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromSource", ICodeCompiler, CompilerResults, CompilerParameters, builder.String));
		}

		public void Add_ICodeCompiler_CompileAssemblyFromSourceBatch(MethodDef oldMethod) {
			if (oldMethod == null)
				return;
			Add(oldMethod, builder.InstanceMethod("CompileAssemblyFromSourceBatch", ICodeCompiler, CompilerResults, CompilerParameters, StringArray));
		}
	}
}
