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

namespace Test.Rename {
	namespace test1 {
		public class Class1 : Test.Rename.Dll.test.pub1.Class1 {
			public override int Prop2 {
				get { return 1; }
			}
			public override int Prop3 {
				set { }
			}
			public override void meth1(int i) { }
			public override void meth1(string s) { }
		}
	}

	namespace test2 {
		public class Class1 : Test.Rename.Dll.test.pub2.IFace1 {
			public void meth1(int i) { }
			public void meth1(string s) { }
		}
		public class Class2 : Test.Rename.Dll.test.pub2.IFace1 {
			void Test.Rename.Dll.test.pub2.IFace1.meth1(int i) { }
			void Test.Rename.Dll.test.pub2.IFace1.meth1(string s) { }
		}
	}

	namespace test3 {
		public class Class1 : Test.Rename.Dll.test.pub3.Class1<int>.IFace1<string> {
			public void meth1(int t) { }
			public void meth1(string u) { }
			public void meth1<V>(V v) { }
		}
		public class Class2 : Test.Rename.Dll.test.pub3.Class1<int>.IFace1<string> {
			void Test.Rename.Dll.test.pub3.Class1<int>.IFace1<string>.meth1(int t) { }
			void Test.Rename.Dll.test.pub3.Class1<int>.IFace1<string>.meth1(string u) { }
			void Test.Rename.Dll.test.pub3.Class1<int>.IFace1<string>.meth1<V>(V v) { }
		}
	}

	namespace test4 {
		public interface IFace {
			void meth1(int i);
			void meth1(string s);
		}
		public class Class1 : Test.Rename.Dll.test.pub4.Class1.EnclosedClass, IFace {
			public override void meth1() { }
			public void meth1(string s) { }
		}
	}

	namespace test5 {
		public class Class1 : Test.Rename.Dll.test.pub5.Class1, Test.Rename.Dll.test.pub5.IFace1 {
			// The C# compiler will create a private virtual method for us, calling Class1's
			// non-virtual method.
		}
	}

	class Program {
		static void Main(string[] args) {
		}
	}
}
