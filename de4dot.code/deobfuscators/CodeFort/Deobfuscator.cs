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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeFort {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CodeFort";
		public const string THE_TYPE = "cf";
		const string DEFAULT_REGEX = @"!^[a-zA-Z]{1,3}$&!^[_<>{}$.`-]$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption dumpEmbeddedAssemblies;

		public DeobfuscatorInfo() : base(DEFAULT_REGEX) =>
			dumpEmbeddedAssemblies = new BoolOption(null, MakeArgName("embedded"), "Dump embedded assemblies", true);

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.Get(),
				DumpEmbeddedAssemblies = dumpEmbeddedAssemblies.Get(),
			});

		protected override IEnumerable<Option> GetOptionsInternal() =>
			new List<Option>() {
				dumpEmbeddedAssemblies,
			};
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

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => DeobfuscatorInfo.THE_NAME;

		public Deobfuscator(Options options) : base(options) => this.options = options;

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(proxyCallFixer.Detected) +
					ToInt32(stringDecrypter.Detected) +
					ToInt32(assemblyDecrypter.Detected);
			if (sum > 0)
				val += 100 + 10 * (sum - 1);

			return val;
		}

		protected override void ScanForObfuscator() {
			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.FindDelegateCreator();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find();
			assemblyDecrypter = new AssemblyDecrypter(module);
			assemblyDecrypter.Find();
		}

		public override bool GetDecryptedModule(int count, ref byte[] newFileData, ref DumpedMethods dumpedMethods) {
			if (count != 0 || !assemblyDecrypter.EncryptedDetected)
				return false;

			newFileData = assemblyDecrypter.Decrypt();
			return newFileData != null;
		}

		public override IDeobfuscator ModuleReloaded(ModuleDefMD module) {
			var newOne = new Deobfuscator(options);
			newOne.SetModule(module);
			newOne.proxyCallFixer = new ProxyCallFixer(module);
			newOne.proxyCallFixer.FindDelegateCreator();
			newOne.stringDecrypter = new StringDecrypter(module);
			newOne.stringDecrypter.Find();
			newOne.assemblyDecrypter = new AssemblyDecrypter(module, assemblyDecrypter);
			newOne.assemblyDecrypter.Find();
			return newOne;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			staticStringInliner.Add(stringDecrypter.Method, (method, gim, args) => stringDecrypter.Decrypt((string)args[0]));
			DeobfuscatedFile.StringDecryptersAdded();

			proxyCallFixer.Find();
			cfMethodCallInliner = new CfMethodCallInliner(proxyCallFixer);

			DumpEmbeddedAssemblies();
		}

		void DumpEmbeddedAssemblies() {
			if (assemblyDecrypter.MainAssemblyHasAssemblyResolver && !options.DumpEmbeddedAssemblies)
				return;
			foreach (var info in assemblyDecrypter.GetAssemblyInfos(DeobfuscatedFile, this)) {
				DeobfuscatedFile.CreateAssemblyFile(info.data, info.asmSimpleName, info.extension);
				AddResourceToBeRemoved(info.resource, $"Embedded assembly: {info.asmFullName}");
			}
			AddCctorInitCallToBeRemoved(assemblyDecrypter.InitMethod);
			AddCallToBeRemoved(module.EntryPoint, assemblyDecrypter.InitMethod);
			AddTypeToBeRemoved(assemblyDecrypter.Type, "Assembly resolver type");
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			proxyCallFixer.Deobfuscate(blocks);
			InlineMethods(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		void InlineMethods(Blocks blocks) {
			cfMethodCallInliner.DeobfuscateBegin(blocks);
			cfMethodCallInliner.Deobfuscate(blocks.MethodBlocks.GetAllBlocks());
		}

		public override void DeobfuscateEnd() {
			RemoveProxyDelegates(proxyCallFixer);
			AddTypeToBeRemoved(proxyCallFixer.ProxyMethodsType, "Type with proxy methods");
			if (CanRemoveStringDecrypterType)
				AddTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
			base.DeobfuscateEnd();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MDToken.ToInt32());
			return list;
		}
	}
}
