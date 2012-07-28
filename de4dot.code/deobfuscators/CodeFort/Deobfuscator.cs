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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.CodeFort {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeFort";
		public const string THE_TYPE = "cf";
		const string DEFAULT_REGEX = @"!^[a-zA-Z]{1,3}$&!^[_<>{}$.`-]$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		BoolOption dumpEmbeddedAssemblies;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			dumpEmbeddedAssemblies = new BoolOption(null, makeArgName("embedded"), "Dump embedded assemblies", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				dumpEmbeddedAssemblies,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		ProxyCallFixer proxyCallFixer;
		StringDecrypter stringDecrypter;
		AssemblyDecrypter assemblyDecrypter;
		CfMethodCallInliner cfMethodCallInliner;

		internal class Options : OptionsBase {
			public bool DumpEmbeddedAssemblies { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(proxyCallFixer.Detected) +
					toInt32(stringDecrypter.Detected) +
					toInt32(assemblyDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void scanForObfuscator() {
			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.findDelegateCreator();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
			assemblyDecrypter = new AssemblyDecrypter(module);
			assemblyDecrypter.find();
		}

		public override bool getDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !assemblyDecrypter.EncryptedDetected)
				return false;

			newFileData = assemblyDecrypter.decrypt();
			return newFileData != null;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.proxyCallFixer = new ProxyCallFixer(module);
			newOne.proxyCallFixer.findDelegateCreator();
			newOne.stringDecrypter = new StringDecrypter(module);
			newOne.stringDecrypter.find();
			newOne.assemblyDecrypter = new AssemblyDecrypter(module, assemblyDecrypter);
			newOne.assemblyDecrypter.find();
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			staticStringInliner.add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.decrypt((string)args[0]));
			DeobfuscatedFile.stringDecryptersAdded();

			proxyCallFixer.find();
			cfMethodCallInliner = new CfMethodCallInliner(proxyCallFixer);

			dumpEmbeddedAssemblies();
		}

		void dumpEmbeddedAssemblies() {
			if (assemblyDecrypter.MainAssemblyHasAssemblyResolver && !options.DumpEmbeddedAssemblies)
				return;
			foreach (var info in assemblyDecrypter.getAssemblyInfos(DeobfuscatedFile, this)) {
				DeobfuscatedFile.createAssemblyFile(info.data, info.asmSimpleName, info.extension);
				addResourceToBeRemoved(info.resource, string.Format("Embedded assembly: {0}", info.asmFullName));
			}
			addCctorInitCallToBeRemoved(assemblyDecrypter.InitMethod);
			addCallToBeRemoved(module.EntryPoint, assemblyDecrypter.InitMethod);
			addTypeToBeRemoved(assemblyDecrypter.Type, "Assembly resolver type");
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.deobfuscate(blocks);
			inlineMethods(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		void inlineMethods(Blocks blocks) {
			cfMethodCallInliner.deobfuscateBegin(blocks);
			cfMethodCallInliner.deobfuscate(blocks.MethodBlocks.getAllBlocks());
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyCallFixer);
			addTypeToBeRemoved(proxyCallFixer.ProxyMethodsType, "Type with proxy methods");
			if (CanRemoveStringDecrypterType)
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
			base.deobfuscateEnd();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MetadataToken.ToInt32());
			return list;
		}
	}
}
