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
using Mono.MyStuff;
using de4dot.blocks;
using de4dot.code.PE;

namespace de4dot.code.deobfuscators.CliSecure {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "CliSecure";
		public const string THE_TYPE = "cs";
		const string DEFAULT_REGEX = @"[a-zA-Z_0-9>}$]$";
		BoolOption decryptMethods;
		BoolOption decryptResources;
		BoolOption removeStackFrameHelper;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			decryptMethods = new BoolOption(null, makeArgName("methods"), "Decrypt methods", true);
			decryptResources = new BoolOption(null, makeArgName("rsrc"), "Decrypt resources", true);
			removeStackFrameHelper = new BoolOption(null, makeArgName("stack"), "Remove all StackFrameHelper code", true);
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
				DecryptMethods = decryptMethods.get(),
				DecryptResources = decryptResources.get(),
				RemoveStackFrameHelper = removeStackFrameHelper.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				decryptMethods,
				decryptResources,
				removeStackFrameHelper,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;

		TypeDefinition cliSecureAttribute;
		ProxyDelegateFinder proxyDelegateFinder;
		CliSecureRtType cliSecureRtType;
		StringDecrypter stringDecrypter;

		ResourceDecrypter resourceDecrypter;
		StackFrameHelper stackFrameHelper;

		internal class Options : OptionsBase {
			public bool DecryptMethods { get; set; }
			public bool DecryptResources { get; set; }
			public bool RemoveStackFrameHelper { get; set; }
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return TypeLong; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		protected override int detectInternal() {
			int val = 0;

			if (cliSecureRtType.Detected || cliSecureAttribute != null)
				val += 100;
			if (stringDecrypter.Detected)
				val += 10;
			if (proxyDelegateFinder.Detected)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			findCliSecureAttribute();
			cliSecureRtType = new CliSecureRtType(module);
			cliSecureRtType.find();
			stringDecrypter = new StringDecrypter(module, cliSecureRtType.StringDecrypterMethod);
			stringDecrypter.find();
			proxyDelegateFinder = new ProxyDelegateFinder(module);
			proxyDelegateFinder.findDelegateCreator();
		}

		void findCliSecureAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName == "SecureTeam.Attributes.ObfuscatedByCliSecureAttribute") {
					cliSecureAttribute = type;
					break;
				}
			}
		}

		public override bool getDecryptedModule(ref byte[] newFileData, ref Dictionary<uint, DumpedMethod> dumpedMethods) {
			if (!options.DecryptMethods)
				return false;

			byte[] fileData = DeobUtils.readModule(module);
			var peImage = new PeImage(fileData);

			if (!new MethodsDecrypter().decrypt(peImage, ref dumpedMethods)) {
				Log.v("Methods aren't encrypted or invalid signature");
				return false;
			}

			newFileData = fileData;
			return true;
		}

		public override IDeobfuscator moduleReloaded(ModuleDefinition module) {
			var newOne = new Deobfuscator(options);
			newOne.setModule(module);
			newOne.cliSecureAttribute = DeobUtils.lookup(module, cliSecureAttribute, "Could not find CliSecure attribute");
			newOne.cliSecureRtType = new CliSecureRtType(module, cliSecureRtType);
			newOne.stringDecrypter = new StringDecrypter(module, stringDecrypter);
			newOne.proxyDelegateFinder = new ProxyDelegateFinder(module, proxyDelegateFinder);
			return newOne;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			addAttributeToBeRemoved(cliSecureAttribute, "Obfuscator attribute");

			resourceDecrypter = new ResourceDecrypter(module);
			resourceDecrypter.find();
			stackFrameHelper = new StackFrameHelper(module);
			stackFrameHelper.find();

			foreach (var type in module.Types) {
				if (type.FullName == "InitializeDelegate" && DotNetUtils.derivesFromDelegate(type))
					this.addTypeToBeRemoved(type, "Obfuscator type");
			}

			proxyDelegateFinder.find();

			staticStringInliner.add(stringDecrypter.Method, (method, args) => stringDecrypter.decrypt((string)args[0]));

			if (options.DecryptMethods) {
				addCctorInitCallToBeRemoved(cliSecureRtType.InitializeMethod);
				addCctorInitCallToBeRemoved(cliSecureRtType.PostInitializeMethod);
				findPossibleNamesToRemove(cliSecureRtType.LoadMethod);
			}
			if (options.DecryptResources)
				addCctorInitCallToBeRemoved(resourceDecrypter.RsrcRrrMethod);
		}

		void decryptResources() {
			var rsrc = resourceDecrypter.mergeResources();
			if (rsrc == null)
				return;
			addResourceToBeRemoved(rsrc, "Encrypted resources");
			addTypeToBeRemoved(resourceDecrypter.Type, "Resource decrypter type");
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			proxyDelegateFinder.deobfuscate(blocks);
			removeStackFrameHelperCode(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			if (options.DecryptResources)
				decryptResources();
			removeProxyDelegates(proxyDelegateFinder);
			if (options.RemoveStackFrameHelper) {
				if (stackFrameHelper.ExceptionLoggerRemover.NumRemovedExceptionLoggers > 0)
					addTypeToBeRemoved(stackFrameHelper.Type, "StackFrameHelper type");
			}
			if (Operations.DecryptStrings != OpDecryptString.None) {
				addTypeToBeRemoved(stringDecrypter.Type, "String decrypter type");
				if (options.DecryptMethods)
					addTypeToBeRemoved(cliSecureRtType.Type, "Obfuscator type");
			}
			if (options.DecryptMethods) {
				addResources("Obfuscator protection files");
				addModuleReferences("Obfuscator protection files");
			}

			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			if (stringDecrypter.Method != null)
				list.Add(stringDecrypter.Method.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}

		void removeStackFrameHelperCode(Blocks blocks) {
			if (!options.RemoveStackFrameHelper)
				return;
			if (stackFrameHelper.ExceptionLoggerRemover.remove(blocks))
				Log.v("Removed StackFrameHelper code");
		}
	}
}
