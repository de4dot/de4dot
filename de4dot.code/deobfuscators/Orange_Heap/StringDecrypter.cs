/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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

namespace de4dot.code.deobfuscators.Orange_Heap
{
    using de4dot.blocks;
    using de4dot.code;
    using de4dot.code.deobfuscators;
    using dnlib.DotNet;
    using dnlib.DotNet.Emit;
    using System;
    using System.Collections.Generic;

    internal class StringDecrypter
    {
        private ModuleDefMD module;
        private MethodDefAndDeclaringTypeDict<StringDecrypterInfo> stringDecrypterMethods = new MethodDefAndDeclaringTypeDict<StringDecrypterInfo>();
        private TypeDef stringDecrypterType;

        public StringDecrypter(ModuleDefMD module)
        {
            this.module = module;
        }

        public string Decrypt(string str)
        {
            char[] chArray = new char[str.Length];
            int index = 0;
            foreach (char ch in str)
            {
                int num3 = index;
                index = num3 + 1;
                chArray[index] = char.ConvertFromUtf32((((byte) ((ch >> 8) ^ index)) << 8) | ((byte) (ch ^ (chArray.Length - num3))))[0];
            }
            return string.Intern(new string(chArray));
        }

        public void Find(ISimpleDeobfuscator simpleDeobfuscator)
        {
            foreach (TypeDef def in this.module.GetTypes())
            {
                this.FindStringDecrypterMethods(def, simpleDeobfuscator);
            }
        }

        private void FindStringDecrypterMethods(TypeDef type, ISimpleDeobfuscator simpleDeobfuscator)
        {
            string[] argsTypes = new string[] { "System.String" };
            foreach (MethodDef def in DotNetUtils.FindMethods(type.Methods, "System.String", argsTypes))
            {
                if (!def.Body.HasExceptionHandlers && (DotNetUtils.GetMethodCalls(def, "System.String System.String::Intern(System.String)") == 1))
                {
                    int num2;
                    simpleDeobfuscator.Deobfuscate(def);
                    IList<Instruction> instructions = def.Body.Instructions;
                    for (int i = 0; i < (instructions.Count - 3); i = num2 + 1)
                    {
                        if ((((instructions[i].IsLdarg() && (instructions[i].GetParameterIndex() <= 0)) && (instructions[i + 1].OpCode.Code == Code.Callvirt)) && (instructions[i + 2].IsStloc() && instructions[i + 3].IsLdloc())) && ((((instructions[i + 4].OpCode.Code == Code.Newarr) && instructions[i + 5].IsStloc()) && (instructions[i + 6].IsLdcI4() && instructions[i + 7].IsStloc())) && (((instructions[i + 8].OpCode.Code == Code.Br_S) && instructions[i + 9].IsLdarg()) && (instructions[i + 10].IsLdloc() && (instructions[i + 11].OpCode.Code == Code.Callvirt)))))
                        {
                            StringDecrypterInfo info = new StringDecrypterInfo(def);
                            this.stringDecrypterMethods.Add(info.method, info);
                            this.stringDecrypterType = def.DeclaringType;
                            object[] args = new object[] { de4dot.code.Utils.RemoveNewlines(info.method) };
                            Logger.v("Found string decrypter method", args);
                            break;
                        }
                        num2 = i;
                    }
                }
            }
        }

        public bool Detected
        {
            get
            {
                return (this.stringDecrypterMethods.Count > 0);
            }
        }

        public IEnumerable<StringDecrypterInfo> StringDecrypterInfos
        {
            get
            {
                return this.stringDecrypterMethods.GetValues();
            }
        }

        public IEnumerable<MethodDef> StringDecrypters
        {
            get
            {
                List<MethodDef> list = new List<MethodDef>(this.stringDecrypterMethods.Count);
                foreach (StringDecrypterInfo info in this.stringDecrypterMethods.GetValues())
                {
                    list.Add(info.method);
                }
                return list;
            }
        }

        public TypeDef Type
        {
            get
            {
                return this.stringDecrypterType;
            }
        }

        public class StringDecrypterInfo
        {
            public MethodDef method;

            public StringDecrypterInfo(MethodDef method)
            {
                this.method = method;
            }
        }
    }
}

