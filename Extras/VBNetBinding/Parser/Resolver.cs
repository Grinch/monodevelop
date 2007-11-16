//  Resolver.cs
//
//  This file was derived from a file from #Develop. 
//
//  Copyright (C) 2001-2007 Andrea Paatz <andrea@icsharpcode.net>
// 
//  This program is free software; you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation; either version 2 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU General Public License for more details.
//  
//  You should have received a copy of the GNU General Public License
//  along with this program; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

using MonoDevelop.Core;
using MonoDevelop.Projects.Parser;
using MonoDevelop.Projects;
using VBBinding.Parser.SharpDevelopTree;

using ClassType = MonoDevelop.Projects.Parser.ClassType;
using ICSharpCode.NRefactory.Visitors;
using ICSharpCode.NRefactory.Parser;
using ICSharpCode.NRefactory.Ast;
using ICSharpCode.NRefactory;

namespace VBBinding.Parser
{
	public class Resolver
	{
		IParserContext parserContext;
		ICompilationUnit cu;
		IClass callingClass;
		LookupTableVisitor lookupTableVisitor;
		
		public Resolver (IParserContext parserContext)
		{
			this.parserContext = parserContext;
		}
		
		public IParserContext ParserContext {
			get {
				return parserContext;
			}
		}
		
		public ICompilationUnit CompilationUnit {
			get {
				return cu;
			}
		}
		
		public IClass CallingClass {
			get {
				return callingClass;
			}
		}
		
		bool showStatic = false;
		
		bool inNew = false;
		
		public bool ShowStatic {
			get {
				return showStatic;
			}
			
			set {
				showStatic = value;
			}
		}
		
		int caretLine;
		int caretColumn;
		
		public IReturnType internalResolve (string expression, int caretLineNumber, int caretColumn, string fileName, string fileContent)
		{
			try{
			//Console.WriteLine("Start Resolving " + expression);
			if (expression == null) {
				return null;
			}
			expression = expression.TrimStart(null);
			if (expression == "") {
				return null;
			}
			this.caretLine     = caretLineNumber;
			this.caretColumn   = caretColumn;
			
			IParseInformation parseInfo = parserContext.GetParseInformation(fileName);
			ICSharpCode.NRefactory.Ast.CompilationUnit fileCompilationUnit = parseInfo.MostRecentCompilationUnit.Tag as ICSharpCode.NRefactory.Ast.CompilationUnit;
			if (fileCompilationUnit == null) {
				ICSharpCode.NRefactory.IParser fileParser = ParserFactory.CreateParser(SupportedLanguage.VBNet, new StringReader (fileContent));
				fileParser.Parse();
				//Console.WriteLine("!Warning: no parseinformation!");
				return null;
			}
			/*
			//// try to find last expression in original string, it could be like " if (act!=null) act"
			//// in this case only "act" should be parsed as expression  
			!!is so!! don't change things that work
			Expression expr=null;	// tentative expression
			Lexer l=null;
			ICSharpCode.NRefactory.Parser.Parser p = new ICSharpCode.NRefactory.Parser.Parser();
			while (expression.Length > 0) {
				l = new Lexer(new StringReader(expression));
				expr = p.ParseExpression(l);
				if (l.LookAhead.val != "" && expression.LastIndexOf(l.LookAhead.val) >= 0) {
					if (expression.Substring(expression.LastIndexOf(l.LookAhead.val) + l.LookAhead.val.Length).Length > 0) 
						expression=expression.Substring(expression.LastIndexOf(l.LookAhead.val) + l.LookAhead.val.Length).Trim();
					else {
						expression=l.LookAhead.val.Trim();
						l=new Lexer(new StringReader(expression));
						expr=p.ParseExpression(l);
						break;
					}
				} else {
					if (l.Token.val!="" || expr!=null) break;
				}
			}
			//// here last subexpression should be fixed in expr
			if it should be changed in expressionfinder don't fix it here
			*/
			ICSharpCode.NRefactory.IParser p = ParserFactory.CreateParser (SupportedLanguage.VBNet, new StringReader(expression));
			Expression expr = p.ParseExpression();
			if (expr == null) {
				return null;
			//}else{
				//Console.WriteLine(expr.ToString());
			}
			lookupTableVisitor = new LookupTableVisitor(SupportedLanguage.VBNet);
			lookupTableVisitor.VisitCompilationUnit (fileCompilationUnit, null);
			//Console.WriteLine("Visited lookup table");
			
			TypeVisitor typeVisitor = new TypeVisitor(this);
			
			VBNetVisitor vbVisitor = new VBNetVisitor();
			cu = (ICompilationUnit)vbVisitor.VisitCompilationUnit(fileCompilationUnit, null);
			//Console.WriteLine("Visited VBNetVisitor");
			if (cu != null) {
				callingClass = GetInnermostClass();
				//Console.WriteLine("CallingClass is " + callingClass == null ? "null" : callingClass.Name);
			}
//			Console.WriteLine("expression = " + expr.ToString());
			IReturnType type = expr.AcceptVisitor(typeVisitor, null) as IReturnType;
			//Console.WriteLine("type visited");
			if (type == null || type.PointerNestingLevel != 0) {
				//Console.WriteLine("Type == null || type.PointerNestingLevel != 0");
				//if (type != null) {
					//Console.WriteLine("Accepted visitor: " + type.FullyQualifiedName);
					//Console.WriteLine("PointerNestingLevel is " + type.PointerNestingLevel);
				//} else {
					//Console.WriteLine("Type == null");
				//}
				
				//// when type is null might be file needs to be reparsed - some vars were lost
				fileCompilationUnit=parserContext.ParseFile(fileName, fileContent).MostRecentCompilationUnit.Tag 
					as ICSharpCode.NRefactory.Ast.CompilationUnit;
				lookupTableVisitor.VisitCompilationUnit(fileCompilationUnit,null);
				//Console.WriteLine("Lookup table visited again");
				
				cu = (ICompilationUnit)vbVisitor.VisitCompilationUnit(fileCompilationUnit, null);
				if (cu != null) {
					callingClass = GetInnermostClass();
					//Console.WriteLine("Got new cu, calling class = " + callingClass.FullyQualifiedName);
				}
				type=expr.AcceptVisitor(typeVisitor,null) as IReturnType;
				//Console.WriteLine("Type visited again");
				if (type==null)	return null;
			}
			if (type.ArrayDimensions != null && type.ArrayDimensions.Length > 0) {
				type = new ReturnType("System.Array");
			}
			//Console.WriteLine("Here: Type is " + type.FullyQualifiedName);
			return type;
			}catch(Exception){
				//Console.WriteLine("Exception in internalResolve: " + ex.Message);
				//Console.WriteLine(ex.StackTrace);
				return null;
			}
		}
		
		/// <remarks>
		/// Returns the innerst class in which the carret currently is, returns null
		/// if the carret is outside any class boundaries.
		/// </remarks>
		IClass GetInnermostClass()
		{
			if (cu != null) {
				foreach (IClass c in cu.Classes) {
					if (c != null && c.Region != null && c.Region.IsInside(caretLine, caretColumn)) {
						return GetInnermostClass(c);
					}
				}
			}
			return null;
		}
		
		IClass GetInnermostClass(IClass curClass)
		{
			if (curClass == null) {
				return null;
			}
			if (curClass.InnerClasses == null) {
				return GetResolvedClass (curClass);
			}
			foreach (IClass c in curClass.InnerClasses) {
				if (c != null && c.Region != null && c.Region.IsInside(caretLine, caretColumn)) {
					return GetInnermostClass(c);
				}
			}
			return GetResolvedClass (curClass);
		}
		
		
		public IClass GetResolvedClass (IClass cls)
		{
			// Returns an IClass in which all type names have been properly resolved
			return parserContext.GetClass (cls.FullyQualifiedName,true,false);
		}

		public LanguageItemCollection IsAsResolve (string expression, int caretLine, int caretColumn, string fileName, string fileContent)
		{
			//Console.WriteLine("Entering IsAsResolve for " + expression);
			LanguageItemCollection result = new LanguageItemCollection ();
			this.caretLine = caretLine;
			this.caretColumn = caretColumn;
			
			IParseInformation parseInfo = parserContext.GetParseInformation (fileName);
			ICSharpCode.NRefactory.Ast.CompilationUnit fcu = parseInfo.MostRecentCompilationUnit.Tag as ICSharpCode.NRefactory.Ast.CompilationUnit;
			if (fcu == null)
				return null;
			ICSharpCode.NRefactory.IParser p = ParserFactory.CreateParser (SupportedLanguage.VBNet, new StringReader (expression));
			Expression expr = p.ParseExpression();
			if (expr == null)
				return null;

			lookupTableVisitor = new LookupTableVisitor (SupportedLanguage.VBNet);
			lookupTableVisitor.VisitCompilationUnit (fcu, null);

			TypeVisitor typeVisitor = new TypeVisitor (this);

			VBNetVisitor vbVisitor = new VBNetVisitor ();
			cu = (ICompilationUnit)vbVisitor.VisitCompilationUnit (fcu, null);
			if (cu != null) {
				callingClass = GetInnermostClass ();
			}

			IReturnType type = expr.AcceptVisitor (typeVisitor, null) as IReturnType;
			if (type == null || type.PointerNestingLevel != 0) {
				fcu = parserContext.ParseFile (fileName, fileContent).MostRecentCompilationUnit.Tag as ICSharpCode.NRefactory.Ast.CompilationUnit;
				lookupTableVisitor.VisitCompilationUnit (fcu, null);
				cu = (ICompilationUnit)vbVisitor.VisitCompilationUnit (fcu, null);

				if (cu != null) {
					callingClass = GetInnermostClass ();
				}
				type = expr.AcceptVisitor (typeVisitor, null) as IReturnType;
				if (type == null)
					return null;
			}
			if (type.ArrayDimensions != null && type.ArrayDimensions.Length > 0)
				type = new ReturnType ("System.Array");

//			IClass returnClass = SearchType (type.FullyQualifiedName, null, cu);
			IClass returnClass = parserContext.SearchType (type.FullyQualifiedName, null, cu);
			if (returnClass == null)
				return null;

			foreach (IClass iclass in parserContext.GetClassInheritanceTree (returnClass)) {
				if (!result.Contains (iclass))
					result.Add (iclass);
			}
			return result;
		}
		
/***** #D Legacy Code - remove once replacement code is verified *****

		public ResolveResult Resolve(IParserContext parserContext, string expression, int caretLineNumber, int caretColumn, string fileName, string fileContent)
		{
			Console.WriteLine("Entering Resolve for " + expression);
			expression = expression.TrimStart(null);
			expression = expression.ToLower();
			if (expression.StartsWith("new ")) {
				inNew = true;
				expression = expression.Substring(4);
			} else {
				inNew = false;
			}
			//Console.WriteLine("\nStart Resolving expression : >{0}<", expression);
			
			Expression expr = null;
			this.caretLine     = caretLineNumber;
			this.caretColumn   = caretColumn;
			this.parserContext = parserContext;
			IParseInformation parseInfo = parserContext.GetParseInformation(fileName);
			ICSharpCode.NRefactory.Ast.CompilationUnit fileCompilationUnit = parseInfo.MostRecentCompilationUnit.Tag as ICSharpCode.NRefactory.Ast.CompilationUnit;
			if (fileCompilationUnit == null) {
				ICSharpCode.NRefactory.Parser.Parser fileParser = new ICSharpCode.NRefactory.Parser.Parser();
				fileParser.Parse(new Lexer(new StringReader(fileContent)));
				Console.WriteLine("!Warning: no parseinformation!");
				return null;
			}
			VBNetVisitor vBNetVisitor = new VBNetVisitor();
			cu = (ICompilationUnit)vBNetVisitor.Visit(fileCompilationUnit, null);
			if (cu != null) {
				callingClass = GetInnermostClass();
				Console.WriteLine("CallingClass is " + callingClass == null ? "null" : callingClass.Name);
			}else{
				Console.WriteLine("NULL compilation unit!");
			}
			lookupTableVisitor = new LookupTableVisitor();
			lookupTableVisitor.Visit(fileCompilationUnit, null);
			
			if (expression == null || expression == "") {
				expr = WithResolve();
				if (expr == null) {
					return null;
				}
			}
			
			if (expression.StartsWith("imports ")) {
				return ImportsResolve(expression);
			}
			Console.WriteLine("Not in imports >{0}<", expression);
			
			if (InMain()) {
				showStatic = true;
			}
			
			// MyBase and MyClass are no expressions, only MyBase.Identifier and MyClass.Identifier
			if (expression == "mybase") {
				expr = new BaseReferenceExpression();
			} else if (expression == "myclass") {
				expr = new ClassReferenceExpression();
			}
			
			if (expr == null) {
				Lexer l = new Lexer(new StringReader(expression));
				ICSharpCode.NRefactory.Parser.Parser p = new ICSharpCode.NRefactory.Parser.Parser();
				expr = p.ParseExpression(l);
				if (expr == null) {
					Console.WriteLine("Warning: No Expression from parsing!");
					return null;
				}
			}
			
			Console.WriteLine(expr.ToString());
			//TypeVisitor typeVisitor = new TypeVisitor(this);
			//TypeVisitor typeVisitor = new VBBinding.Parser.TypeVisitor(this);
			//IReturnType type = expr.AcceptVisitor(typeVisitor, null) as IReturnType;
			//Console.WriteLine("type visited");
			
			IReturnType type = internalResolve (parserContext, expression, caretLineNumber, caretColumn, fileName, fileContent);
			//IClass returnClass = SearchType (type.FullyQualifiedName, cu);
			
			if (type == null || type.PointerNestingLevel != 0) {
				Console.WriteLine("Type == null || type.PointerNestingLevel != 0");
				if (type != null) {
					Console.WriteLine("PointerNestingLevel is " + type.PointerNestingLevel);
				} else {
					Console.WriteLine("Type == null");
				}
				return null;
			}
			if (type.ArrayDimensions != null && type.ArrayDimensions.Length > 0) {
				type = new ReturnType("System.Array");
			}
			Console.WriteLine("Here: Type is " + type.FullyQualifiedName);
			//IClass returnClass = SearchType(type.FullyQualifiedName, callingClass, cu);
			IClass returnClass = parserContext.GetClass(type.FullyQualifiedName);
			if (returnClass == null) {
				Console.WriteLine("IClass is null! Trying namespace!");
				// Try if type is Namespace:
				string n = SearchNamespace(type.FullyQualifiedName, cu);
				if (n == null) {
					return null;
				}
				ArrayList content = parserContext.GetNamespaceContents(n, true,false);
				ArrayList classes = new ArrayList();
				for (int i = 0; i < content.Count; ++i) {
					if (content[i] is IClass) {
						if (inNew) {
							IClass c = (IClass)content[i];
//							Console.WriteLine("Testing " + c.Name);
							if ((c.ClassType == ClassType.Class) || (c.ClassType == ClassType.Struct)) {
								classes.Add(c);
//								Console.WriteLine("Added");
							}
						} else {
							classes.Add((IClass)content[i]);
						}
					}
				}
				
				Console.WriteLine("Checking subnamespace " + n);
				string[] namespaces = parserContext.GetNamespaceList(n, false);
				Console.WriteLine("Got " + namespaces);
				return new ResolveResult(namespaces, classes);
			}
			Console.WriteLine("Returning Result!");
			if (inNew) {
				return new ResolveResult(returnClass, ListTypes(new ArrayList(), returnClass));
			} else {
				return new ResolveResult(returnClass,ListMembers(new ArrayList(), returnClass));
			}
//			return new ResolveResult(returnClass, ListMembers(new ArrayList(),returnClass));
		}
*/
		
		
		
		
		
		public ResolveResult Resolve (string expression, int caretLineNumber, int caretColumn, string fileName, string fileContent) 
		{
			if (expression == null) {
				return null;
			}
			expression = expression.TrimStart(null);
			if (expression == "") {
				return null;
			}
			if (expression.ToLower().StartsWith("imports")) {
				// expression[expression.Length - 1] != '.'
				// the period that causes this Resove() is not part of the expression
				if (expression[expression.Length - 1] == '.') {
					return null;
				}
				int i;
				for (i = expression.Length - 1; i >= 0; --i) {
					if (!(Char.IsLetterOrDigit(expression[i]) || expression[i] == '_' || expression[i] == '.')) {
						break;
					}
				}
				// no Identifier before the period
				if (i == expression.Length - 1) {
					return null;
				}
				string t = expression.Substring(i + 1);
//				Console.WriteLine("in Imports Statement");
				string[] namespaces = parserContext.GetNamespaceList (t);
				if (namespaces == null || namespaces.Length <= 0) {
					return null;
				}
				return new ResolveResult(namespaces);
			}
			
			//Console.WriteLine("Not in Imports");
			IReturnType type = internalResolve (expression, caretLineNumber, caretColumn, fileName, fileContent);
			IClass returnClass = SearchType (type.FullyQualifiedName, cu);
			if (returnClass == null) {
				// Try if type is Namespace:
				string n = SearchNamespace(type.FullyQualifiedName, cu);
				if (n == null) {
					return null;
				}
				LanguageItemCollection content = parserContext.GetNamespaceContents(n,true,false);
				LanguageItemCollection classes = new LanguageItemCollection();
				for (int i = 0; i < content.Count; ++i) {
					if (content[i] is IClass) {
						classes.Add((IClass)content[i]);
					}
				}
				string[] namespaces = parserContext.GetNamespaceList(n, true, false);
				return new ResolveResult(namespaces, classes);
			}
			//Console.WriteLine("Returning Result!");
			if (returnClass.FullyQualifiedName == "System.Void")
				return null;
			return new ResolveResult(returnClass, ListMembers(new LanguageItemCollection(), returnClass));
		}
		
		
		
		
		
		
		
		
		LanguageItemCollection ListMembers (LanguageItemCollection members, IClass curType)
		{
			//Console.WriteLine("LIST MEMBERS!!!");
			//Console.WriteLine("showStatic = " + showStatic);
			//Console.WriteLine(curType.InnerClasses.Count + " classes");
			//Console.WriteLine(curType.Properties.Count + " properties");
			//Console.WriteLine(curType.Methods.Count + " methods");
			//Console.WriteLine(curType.Events.Count + " events");
			//Console.WriteLine(curType.Fields.Count + " fields");
			if (showStatic) {
				foreach (IClass c in curType.InnerClasses) {
					if (IsAccessible(curType, c)) {
						members.Add(c);
//						Console.WriteLine("Member added");
					}
				}
			}
			foreach (IProperty p in curType.Properties) {
				if (MustBeShown(curType, p)) {
					members.Add(p);
//					Console.WriteLine("Member added");
				} else {
					//// for some public static properties msutbeshowen is false, so additional check
					//// this is lame fix because curType doesn't allow to find out if to show only
					//// static public or simply public properties
					if (((AbstractMember)p).ReturnType!=null) {
						// if public add it to completion window
						if (((AbstractDecoration)p).IsPublic) members.Add(p);
//						Console.WriteLine("Property {0} added", p.FullyQualifiedName);
					}
				}
			}
//			Console.WriteLine("ADDING METHODS!!!");
			foreach (IMethod m in curType.Methods) {
//				Console.WriteLine("Method : " + m);
				if (MustBeShown(curType, m)) {
					members.Add(m);
//					Console.WriteLine("Member added");
				}
			}
			foreach (IEvent e in curType.Events) {
				if (MustBeShown(curType, e)) {
					members.Add(e);
//					Console.WriteLine("Member added");
				}
			}
			foreach (IField f in curType.Fields) {
				if (MustBeShown(curType, f)) {
					members.Add(f);
//					Console.WriteLine("Member added");
				} else {
					//// enum fields must be shown here if present
					if (curType.ClassType == ClassType.Enum) {
						if (IsAccessible(curType,f)) members.Add(f);
//						Console.WriteLine("Member {0} added", f.FullyQualifiedName);
					}
				}
			}
//			Console.WriteLine("ClassType = " + curType.ClassType);
			if (curType.ClassType == ClassType.Interface && !showStatic) {
				foreach (IReturnType s in curType.BaseTypes) {
					IClass baseClass = parserContext.GetClass (s.FullyQualifiedName, true, false);
					if (baseClass != null && baseClass.ClassType == ClassType.Interface) {
						ListMembers(members, baseClass);
					}
				}
			} else {
				IClass baseClass = BaseClass(curType);
				if (baseClass != null) {
//					Console.WriteLine("Base Class = " + baseClass.FullyQualifiedName);
					ListMembers(members, baseClass);
				}
			}
//			Console.WriteLine("listing finished");
			return members;
		}
		
		
		//Hacked from ListMembers - not sure if entirely correct or necessary
		LanguageItemCollection ListTypes (LanguageItemCollection members, IClass curType)
		{
			//Console.WriteLine("LIST TYPES!!!");
			//Console.WriteLine("showStatic = " + showStatic);
			//Console.WriteLine(curType.InnerClasses.Count + " classes");
			if (showStatic) {
				foreach (IClass c in curType.InnerClasses) {
					if (IsAccessible(curType, c)) {
						members.Add(c);
//						Console.WriteLine("Member added");
					}
				}
			}
//			Console.WriteLine("ClassType = " + curType.ClassType);
			if (curType.ClassType == ClassType.Interface && !showStatic) {
				foreach (IReturnType s in curType.BaseTypes) {
					IClass baseClass = parserContext.GetClass (s.FullyQualifiedName, true, false);
					if (baseClass != null && baseClass.ClassType == ClassType.Interface) {
						ListTypes(members, baseClass);
					}
				}
			} else {
				IClass baseClass = BaseClass(curType);
				if (baseClass != null) {
//					Console.WriteLine("Base Class = " + baseClass.FullyQualifiedName);
					ListTypes(members, baseClass);
				}
			}
//			Console.WriteLine("listing finished");
			return members;
		}
		
		bool InMain()
		{
			return false;
		}
		
		Expression WithResolve()
		{
			//Console.WriteLine("in WithResolve");
			Expression expr = null;
			if (lookupTableVisitor.WithStatements != null) {
				//Console.WriteLine("{0} WithStatements", lookupTableVisitor.WithStatements.Count);
				foreach (WithStatement with in lookupTableVisitor.WithStatements) {
//					Console.WriteLine("Position: ({0}/{1})", with.StartLocation, with.EndLocation);
					if (IsInside(new Location(caretColumn, caretLine), with.StartLocation, with.EndLocation)) {
						expr = with.Expression;
					}
				}
			}
//			if (expr == null) {
//				Console.WriteLine("No WithStatement found");
//			} else {
//				Console.WriteLine("WithExpression : " + expr);
//			}
			return expr;
		}
		
		ResolveResult ImportsResolve(string expression)
		{
			// expression[expression.Length - 1] != '.'
			// the period that causes this Resove() is not part of the expression
			if (expression[expression.Length - 1] == '.') {
				return null;
			}
			int i;
			for (i = expression.Length - 1; i >= 0; --i) {
				if (!(Char.IsLetterOrDigit(expression[i]) || expression[i] == '_' || expression[i] == '.')) {
					break;
				}
			}
			// no Identifier before the period
			if (i == expression.Length - 1) {
				return null;
			}
			string t = expression.Substring(i + 1);
//			Console.WriteLine("in imports Statement");
			string[] namespaces = parserContext.GetNamespaceList(t, true, false);
			if (namespaces == null || namespaces.Length <= 0) {
				return null;
			}
			return new ResolveResult(namespaces);
		}
		
		bool InStatic()
		{
			IProperty property = Get();
			if (property != null) {
				return property.IsStatic;
			}
			IMethod method = GetMethod();
			if (method != null) {
				return method.IsStatic;
			}
			return false;
		}
		
		public List<IMethod> SearchMethod(IReturnType type, string memberName)
		{
			if (type == null || type.PointerNestingLevel != 0) {
				return new List<IMethod> ();
			}
			IClass curType;
			if (type.ArrayDimensions != null && type.ArrayDimensions.Length > 0) {
				curType = SearchType("System.Array", null, null);
			} else {
				curType = SearchType(type.FullyQualifiedName, null, null);
				if (curType == null) {
					return new List<IMethod>();
				}
			}
			return SearchMethod(new List<IMethod>(), curType, memberName);
		}
		
		List<IMethod> SearchMethod(List<IMethod> methods, IClass curType, string memberName)
		{
			//bool isClassInInheritanceTree = IsClassInInheritanceTree(curType, callingClass);
			
			foreach (IMethod m in curType.Methods) {
				if (m.Name.ToLower() == memberName.ToLower() &&
				    MustBeShown(curType, m) &&  //,  callingClass, showStatic, isClassInInheritanceTree) &&
				    !((m.Modifiers & ModifierEnum.Override) == ModifierEnum.Override)) {
					methods.Add(m);
				}
			}
			IClass baseClass = BaseClass(curType); //, false);
			if (baseClass != null) {
				return SearchMethod(methods, baseClass, memberName);
			}
			showStatic = false;
			return methods;
		}
		
		public List<IIndexer> SearchIndexer(IReturnType type)
		{
			IClass curType = SearchType(type.FullyQualifiedName, null, null);
			if (curType != null) {
				return SearchIndexer(new List<IIndexer> (), curType);
			}
			return new List<IIndexer> ();
		}
		
		public List<IIndexer> SearchIndexer(List<IIndexer> indexer, IClass curType)
		{
			//bool isClassInInheritanceTree =IsClassInInheritanceTree(curType, callingClass);
			foreach (IIndexer i in curType.Indexer) {
				if (MustBeShown(curType, i) /* , callingClass, showStatic, isClassInInheritanceTree) */ 
				&& !((i.Modifiers & ModifierEnum.Override) == ModifierEnum.Override)) {
					indexer.Add(i);
				}
			}
			IClass baseClass = BaseClass(curType);
			if (baseClass != null) {
				return SearchIndexer(indexer, baseClass);
			}
			showStatic = false;
			return indexer;
		}
		
		// no methods or indexer
		public IReturnType SearchMember(IReturnType type, string memberName)
		{
			if (type == null || memberName == null || memberName == "") {
				return null;
			}
//			Console.WriteLine("searching member {0} in {1}", memberName, type.Name);
			IClass curType = SearchType(type.FullyQualifiedName, callingClass, cu);
			//bool isClassInInheritanceTree =IsClassInInheritanceTree(curType, callingClass);
			
			if (curType == null) {
//				Console.WriteLine("Type not found in SearchMember");
				return null;
			}
			if (type.PointerNestingLevel != 0) {
				return null;
			}
			if (type.ArrayDimensions != null && type.ArrayDimensions.Length > 0) {
				curType = SearchType("System.Array", null, null);
			}
			if (curType.ClassType == ClassType.Enum) {
				foreach (IField f in curType.Fields) {
					if (f.Name.ToLower() == memberName.ToLower() && MustBeShown(curType, f) /* , callingClass, showStatic, isClassInInheritanceTree) */ ) {
						showStatic = false;
						return type; // enum members have the type of the enum
					}
				}
			}
			if (showStatic) {
//				Console.WriteLine("showStatic == true");
				foreach (IClass c in curType.InnerClasses) {
					if (c.Name.ToLower() == memberName.ToLower()  && IsAccessible(curType, c) /*, callingClass, isClassInInheritanceTree) */) {
						return new ReturnType(c.FullyQualifiedName);
					}
				}
			}
//			Console.WriteLine("#Properties " + curType.Properties.Count);
			foreach (IProperty p in curType.Properties) {
//				Console.WriteLine("checke Property " + p.Name);
//				Console.WriteLine("member name " + memberName);
				if (p.Name.ToLower() == memberName.ToLower() && MustBeShown(curType, p) /*, callingClass, showStatic, isClassInInheritanceTree)*/) {
//					Console.WriteLine("Property found " + p.Name);
					showStatic = false;
					return p.ReturnType;
				}
			}
			foreach (IField f in curType.Fields) {
//				Console.WriteLine("checke Feld " + f.Name);
//				Console.WriteLine("member name " + memberName);
				if (f.Name.ToLower() == memberName.ToLower() && MustBeShown(curType, f) /*, callingClass, showStatic, isClassInInheritanceTree)*/) {
//					Console.WriteLine("Field found " + f.Name);
					showStatic = false;
					return f.ReturnType;
				}
			}
			foreach (IEvent e in curType.Events) {
				if (e.Name.ToLower() == memberName.ToLower() && MustBeShown(curType, e) /*, callingClass, showStatic, isClassInInheritanceTree)*/) {
					showStatic = false;
					return e.ReturnType;
				}
			}
			foreach (IMethod m in curType.Methods) {
//				Console.WriteLine("checke Method " + m.Name);
//				Console.WriteLine("member name " + memberName);
				if (m.Name.ToLower() == memberName.ToLower() && MustBeShown(curType, m) /*, callingClass, showStatic, isClassInInheritanceTree) /* check if m has no parameters && m.*/) {
//					Console.WriteLine("Method found " + m.Name);
					showStatic = false;
					return m.ReturnType;
				}
			}
			foreach (IReturnType baseType in curType.BaseTypes) {
				IClass c = SearchType(baseType.FullyQualifiedName, curType);
				if (c != null) {
					IReturnType erg = SearchMember(new ReturnType(c.FullyQualifiedName), memberName);
					if (erg != null) {
						return erg;
					}
				}
			}
			return null;
		}
		
		bool IsInside(Location between, Location start, Location end)
		{
			if (between.Y < start.Y || between.Y > end.Y) {
//				Console.WriteLine("Y = {0} not between {1} and {2}", between.Y, start.Y, end.Y);
				return false;
			}
			if (between.Y > start.Y) {
				if (between.Y < end.Y) {
					return true;
				}
				// between.Y == end.Y
//				Console.WriteLine("between.Y = {0} == end.Y = {1}", between.Y, end.Y);
//				Console.WriteLine("returning {0}:, between.X = {1} <= end.X = {2}", between.X <= end.X, between.X, end.X);
				return between.X <= end.X;
			}
			// between.Y == start.Y
//			Console.WriteLine("between.Y = {0} == start.Y = {1}", between.Y, start.Y);
			if (between.X < start.X) {
				return false;
			}
			// start is OK and between.Y <= end.Y
			return between.Y < end.Y || between.X <= end.X;
		}
		
		ReturnType SearchVariable(string name)
		{
//			Console.WriteLine("Searching Variable");
//			
//			Console.WriteLine("LookUpTable has {0} entries", lookupTableVisitor.variables.Count);
//			Console.WriteLine("Listing Variables:");
//			IDictionaryEnumerator enumerator = lookupTableVisitor.variables.GetEnumerator();
//			while (enumerator.MoveNext()) {
//				Console.WriteLine(enumerator.Key);
//			}
//			Console.WriteLine("end listing");
			List<LocalLookupVariable> variables = lookupTableVisitor.Variables[name.ToLower()];
//			if (variables == null || variables.Count <= 0) {
//				Console.WriteLine(name + " not in LookUpTable");
//				return null;
//			}
			
			ReturnType found = null;
			if (variables != null) {
				foreach (LocalLookupVariable v in variables) {
//					Console.WriteLine("Position: ({0}/{1})", v.StartPos, v.EndPos);
					if (IsInside(new Location(caretColumn, caretLine), v.StartPos, v.EndPos)) {
						found = new ReturnType(v.TypeRef);
//						Console.WriteLine("Variable found");
						break;
					}
				}
			}
//			if (found == null) {
//				Console.WriteLine("No Variable found");
//				return null;
//			}
			return found;
		}
		
		/// <remarks>
		/// does the dynamic lookup for the typeName
		/// </remarks>
		public IReturnType DynamicLookup(string typeName)
		{
//			Console.WriteLine("starting dynamic lookup");
//			Console.WriteLine("name == " + typeName);
			
			// try if it exists a variable named typeName
			ReturnType variable = SearchVariable(typeName);
			if (variable != null) {
				showStatic = false;
				return variable;
			}
//			Console.WriteLine("No Variable found");
			
			if (callingClass == null) {
				return null;
			}
			//// somehow search in callingClass fields is not returning anything, so I am searching here once again
			foreach (IField f in callingClass.Fields) {
				if (f.Name.ToLower() == typeName.ToLower()) {
//					Console.WriteLine("Field found " + f.Name);
					return f.ReturnType;
				}
			}
			//// end of mod for search in Fields
		
			// try if typeName is a method parameter
			IReturnType p = SearchMethodParameter(typeName);
			if (p != null) {
//				Console.WriteLine("MethodParameter Found");
				showStatic = false;
				return p;
			}
//			Console.WriteLine("No Parameter found");
			
			// check if typeName == value in set method of a property
			if (typeName == "value") {
				p = SearchProperty();
				if (p != null) {
					showStatic = false;
					return p;
				}
			}
//			Console.WriteLine("No Property found");
			
			// try if there exists a nonstatic member named typeName
			showStatic = false;
			IReturnType t = SearchMember(callingClass == null ? null : new ReturnType(callingClass.FullyQualifiedName), typeName);
			if (t != null) {
				return t;
			}
//			Console.WriteLine("No nonstatic member found");
			
			// try if there exists a static member named typeName
			showStatic = true;
			t = SearchMember(callingClass == null ? null : new ReturnType(callingClass.FullyQualifiedName), typeName);
			if (t != null) {
				showStatic = false;
				return t;
			}
//			Console.WriteLine("No static member found");
			
			// try if there exists a static member in outer classes named typeName
			ClassCollection classes = GetOuterClasses(); //cu, caretLine, caretColumn);
			foreach (IClass c in classes) {
				t = SearchMember(callingClass == null ? null : new ReturnType(c.FullyQualifiedName), typeName);
				if (t != null) {
					showStatic = false;
					return t;
				}
			}
//			Console.WriteLine("No static member in outer classes found");
//			Console.WriteLine("DynamicLookUp resultless");
			return null;
		}
		
		IProperty Get()
		{
			foreach (IProperty property in callingClass.Properties) {
				if (property.BodyRegion != null && property.BodyRegion.IsInside(caretLine, caretColumn)) {
					return property;
				}
			}
			return null;
		}
		
		IMethod GetMethod()
		{
			foreach (IMethod method in callingClass.Methods) {
				if (method.BodyRegion != null && method.BodyRegion.IsInside(caretLine, caretColumn)) {
					return method;
				}
			}
			return null;
		}
		
		IReturnType SearchProperty()
		{
			IProperty property = Get();
			if (property == null) {
				return null;
			}
			if (property.SetterRegion != null && property.SetterRegion.IsInside(caretLine, caretColumn)) {
				return property.ReturnType;
			}
			return null;
		}
		
		IReturnType SearchMethodParameter(string parameter)
		{
			IMethod method = GetMethod();
			if (method == null) {
				//Console.WriteLine("Method not found");
				return null;
			}
			foreach (IParameter p in method.Parameters) {
				if (p.Name.ToLower() == parameter.ToLower()) {
				//	Console.WriteLine("Parameter found");
					return p.ReturnType;
				}
			}
			return null;
		}
		
		public string SearchNamespace(string name, ICompilationUnit unit)
		{
			try{
			//return parserContext.SearchNamespace(null,name,false); //, unit, caretLine, caretColumn, false);
			if (parserContext.NamespaceExists(name)) {
				return name;
			}
			if (unit == null) {
				//Console.WriteLine("done, resultless");
				//return null;
				return parserContext.SearchNamespace(null,name,false); //, unit, caretLine, caretColumn, false);
			}
			foreach (IUsing u in unit.Usings) {
				if (u != null && (u.Region == null || u.Region.IsInside(caretLine, caretColumn))) {
					string nameSpace = parserContext.SearchNamespace (u, name);
					if (nameSpace != null) {
						return nameSpace;
					}
				}
			}
 			//Console.WriteLine("done, resultless");
			//return null;
			return parserContext.SearchNamespace(null,name,false); //, unit, caretLine, caretColumn, false);
			}catch(Exception){
				//Console.WriteLine("done, resultless");
				return null;
			}
		}
		
		
		/// <remarks>
		/// use the usings and the name of the namespace to find a class
		/// </remarks>
		public IClass SearchType(string name, ICompilationUnit unit)
		{
//			Console.WriteLine("Searching Type " + name);
			if (name == null || name == String.Empty) {
//				Console.WriteLine("No Name!");
				return null;
			}
			IClass c;
			c = parserContext.GetClass(name,true,false);
			if (c != null) {
//				Console.WriteLine("Found!");
				return c;
			}
			//Console.WriteLine("No FullName");
			if (unit != null) {
				//Console.WriteLine(unit.Usings.Count + " Usings");
				foreach (IUsing u in unit.Usings) {
					if (u != null && (u.Region == null || u.Region.IsInside(caretLine, caretColumn))) {
//						Console.WriteLine("In UsingRegion");
						c = parserContext.SearchType(u, name);
						if (c != null) {
//							Console.WriteLine("SearchType Successfull!!!");
							return c;
						}
					}
				}
			}
			if (callingClass == null) {
				//Console.WriteLine("NULL calling class!");
				return null;
			}
			string fullname = callingClass.FullyQualifiedName;
			string[] namespaces = fullname.Split(new char[] {'.'});
			string curnamespace = "";
			int i = 0;
			
			do {
				curnamespace += namespaces[i] + '.';
				c = parserContext.GetClass(curnamespace + name,true,false);
				if (c != null) {
					return c;
				}
				i++;
			}
			while (i < namespaces.Length);
			
			return null;
		}
		
		/// <remarks>
		/// use the usings and the name of the namespace to find a class
		/// </remarks>
		public IClass SearchType(string name, IClass curType)
		{
			return parserContext.SearchType(name, curType,null); //, caretLine, caretColumn, false);
		}
		
		/// <remarks>
		/// use the usings and the name of the namespace to find a class
		/// </remarks>
		public IClass SearchType(string name, IClass curType, ICompilationUnit unit)
		{
			return parserContext.SearchType(name, curType,unit); //, unit, caretLine, caretColumn, false);
		}
		
		public LanguageItemCollection CtrlSpace (int caretLine, int caretColumn, string fileName)
		{
			//Console.WriteLine("Entering CtrlSpace for " + caretLine + ":" + caretColumn + " in " + fileName);
			LanguageItemCollection result = new LanguageItemCollection();
			foreach (KeyValuePair<string, string> pt in TypeReference.PrimitiveTypesVB)
				result.Add (new Namespace (pt.Key));
				
			IParseInformation parseInfo = parserContext.GetParseInformation(fileName);
			ICSharpCode.NRefactory.Ast.CompilationUnit fileCompilationUnit = parseInfo.MostRecentCompilationUnit.Tag as ICSharpCode.NRefactory.Ast.CompilationUnit;
			if (fileCompilationUnit == null) {
				//Console.WriteLine("!Warning: no parseinformation!");
				return null;
			}
			LookupTableVisitor lookupTableVisitor = new LookupTableVisitor(SupportedLanguage.VBNet);
			lookupTableVisitor.VisitCompilationUnit (fileCompilationUnit, null);
			VBNetVisitor vBNetVisitor = new VBNetVisitor();
			cu = (ICompilationUnit)vBNetVisitor.VisitCompilationUnit(fileCompilationUnit, null);
			if (cu != null) {
				callingClass = GetInnermostClass();
				//Console.WriteLine("CallingClass is " + callingClass == null ? "null" : callingClass.Name);
			}
			foreach (string name in lookupTableVisitor.Variables.Keys) {
				List<LocalLookupVariable> variables = lookupTableVisitor.Variables[name.ToLower()];
				if (variables != null && variables.Count > 0) {
					foreach (LocalLookupVariable v in variables) {
						if (IsInside(new Location(caretColumn, caretLine), v.StartPos, v.EndPos)) {
							result.Add(new DefaultParameter (null, name, new ReturnType (v.TypeRef.SystemType)));
							break;
						}
					}
				}
			}
			if (callingClass != null) {
				//result = parserContext.ListMembers(result, callingClass, callingClass, InStatic());
				result=ListMembers(result,callingClass);
			}
			string n = "";
			result.AddRange(parserContext.GetNamespaceContents(n, true,false));
			foreach (IUsing u in cu.Usings) {
				if (u != null && (u.Region == null || u.Region.IsInside(caretLine, caretColumn))) {
					foreach (string name in u.Usings) {
						result.AddRange(parserContext.GetNamespaceContents(name,true, false));
					}
					foreach (string alias in u.Aliases) {
						result.Add (new Namespace (alias));
					}
				}
			}
			return result;
		}
	
	
		public IClass BaseClass(IClass curClass)
		{
			foreach (IReturnType s in curClass.BaseTypes) {
				IClass baseClass = parserContext.GetClass (s.FullyQualifiedName, true, false);
				if (baseClass != null && baseClass.ClassType != ClassType.Interface) {
					return baseClass;
				}
			}
			return null;
		}
		
		bool IsAccessible(IClass c, IDecoration member)
		{
//			Console.WriteLine("member.Modifiers = " + member.Modifiers);
			if ((member.Modifiers & ModifierEnum.Internal) == ModifierEnum.Internal) {
				return false;
			}
			if ((member.Modifiers & ModifierEnum.Public) == ModifierEnum.Public) {
//				Console.WriteLine("IsAccessible");
				return true;
			}
			if ((member.Modifiers & ModifierEnum.Protected) == ModifierEnum.Protected && IsClassInInheritanceTree(c, callingClass)) {
//				Console.WriteLine("IsAccessible");
				return true;
			}
			return c.FullyQualifiedName == callingClass.FullyQualifiedName;
		}
		
		bool MustBeShown(IClass c, IDecoration member)
		{
//			Console.WriteLine("member:" + member.Modifiers);
			if ((!showStatic &&  ((member.Modifiers & ModifierEnum.Static) == ModifierEnum.Static)) ||
			    ( showStatic && !((member.Modifiers & ModifierEnum.Static) == ModifierEnum.Static))) {
				//// enum type fields are not shown here - there is no info in member about enum field
				return false;
			}
//			Console.WriteLine("Testing Accessibility");
			return IsAccessible(c, member);
		}
		
		/// <remarks>
		/// Returns true, if class possibleBaseClass is in the inheritance tree from c
		/// </remarks>
		bool IsClassInInheritanceTree(IClass possibleBaseClass, IClass c)
		{
			if (possibleBaseClass == null || c == null) {
				return false;
			}
			if (possibleBaseClass.FullyQualifiedName == c.FullyQualifiedName) {
				return true;
			}
			foreach (IReturnType baseClass in c.BaseTypes) {
				IClass bc = parserContext.GetClass (baseClass.FullyQualifiedName, true, false);
				if (IsClassInInheritanceTree(possibleBaseClass, bc)) {
					return true;
				}
			}
			return false;
		}
		
		/// <remarks>
		/// Returns all (nestet) classes in which the carret currently is exept
		/// the innermost class, returns an empty collection if the carret is in 
		/// no class or only in the innermost class.
		/// the most outer class is the last in the collection.
		/// </remarks>
		ClassCollection GetOuterClasses()
		{
			ClassCollection classes = new ClassCollection();
			if (cu != null) {
				foreach (IClass c in cu.Classes) {
					if (c != null && c.Region != null && c.Region.IsInside(caretLine, caretColumn)) {
						if (c != GetInnermostClass()) {
							GetOuterClasses(classes, c);
							classes.Add(GetResolvedClass (c));
						}
						break;
					}
				}
			}
			
			return classes;
		}
		
		void GetOuterClasses(ClassCollection classes, IClass curClass)
		{
			if (curClass != null) {
				foreach (IClass c in curClass.InnerClasses) {
					if (c != null && c.Region != null && c.Region.IsInside(caretLine, caretColumn)) {
						if (c != GetInnermostClass()) {
							GetOuterClasses(classes, c);
							classes.Add(GetResolvedClass (c));
						}
						break;
					}
				}
			}
		}
	}
}
