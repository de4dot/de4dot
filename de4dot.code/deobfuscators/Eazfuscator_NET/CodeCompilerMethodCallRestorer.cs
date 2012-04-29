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

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class CodeCompilerMethodCallRestorer : MethodCallRestorerBase {
		TypeReference CodeDomProvider {
			get {
				return builder.type("System.CodeDom.Compiler", "CodeDomProvider", "System");
			}
		}

		TypeReference ICodeCompiler {
			get {
				return builder.type("System.CodeDom.Compiler", "ICodeCompiler", "System");
			}
		}

		TypeReference CompilerResults {
			get {
				return builder.type("System.CodeDom.Compiler", "CompilerResults", "System");
			}
		}

		TypeReference CompilerParameters {
			get {
				return builder.type("System.CodeDom.Compiler", "CompilerParameters", "System");
			}
		}

		TypeReference CodeCompileUnit {
			get {
				return builder.type("System.CodeDom", "CodeCompileUnit", "System");
			}
		}

		TypeReference CodeCompileUnitArray {
			get { return builder.array(CodeCompileUnit); }
		}

		TypeReference StringArray {
			get { return builder.array(builder.String); }
		}

		public CodeCompilerMethodCallRestorer(ModuleDefinition module)
			: base(module) {
		}

		public void add_CodeDomProvider_CompileAssemblyFromDom(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromDom", CodeDomProvider, CompilerResults, CompilerParameters, CodeCompileUnitArray));
		}

		public void add_CodeDomProvider_CompileAssemblyFromFile(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromFile", CodeDomProvider, CompilerResults, CompilerParameters, StringArray));
		}

		public void add_CodeDomProvider_CompileAssemblyFromSource(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromSource", CodeDomProvider, CompilerResults, CompilerParameters, StringArray));
		}

		public void add_ICodeCompiler_CompileAssemblyFromDom(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromDom", ICodeCompiler, CompilerResults, CompilerParameters, CodeCompileUnit));
		}

		public void add_ICodeCompiler_CompileAssemblyFromDomBatch(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromDomBatch", ICodeCompiler, CompilerResults, CompilerParameters, CodeCompileUnitArray));
		}

		public void add_ICodeCompiler_CompileAssemblyFromFile(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromFile", ICodeCompiler, CompilerResults, CompilerParameters, builder.String));
		}

		public void add_ICodeCompiler_CompileAssemblyFromFileBatch(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromFileBatch", ICodeCompiler, CompilerResults, CompilerParameters, StringArray));
		}

		public void add_ICodeCompiler_CompileAssemblyFromSource(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromSource", ICodeCompiler, CompilerResults, CompilerParameters, builder.String));
		}

		public void add_ICodeCompiler_CompileAssemblyFromSourceBatch(MethodDefinition oldMethod) {
			if (oldMethod == null)
				return;
			add(oldMethod, builder.instanceMethod("CompileAssemblyFromSourceBatch", ICodeCompiler, CompilerResults, CompilerParameters, StringArray));
		}
	}
}
