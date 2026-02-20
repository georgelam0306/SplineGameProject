// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pooled.Generator
{
	[Generator]
	public sealed class PooledGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var schemas = context.SyntaxProvider.ForAttributeWithMetadataName(
				fullyQualifiedMetadataName: "Pooled.PooledAttribute",
				predicate: static (n, _) => n is StructDeclarationSyntax,
				transform: static (ctx, _) => Transform(ctx))
				.Where(static m => m is not null);

			context.RegisterSourceOutput(schemas, static (spc, modelObj) =>
			{
				var model = (SchemaModel)modelObj!;
				try
				{
					var source = SourceRenderer.Render(model);
					var hint = model.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty).Replace('.', '_') + ".Pooled.g.cs";
					spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
				}
				catch (Exception ex)
				{
					spc.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("POOLED000", "Generator error", ex.ToString(), "Pooled", DiagnosticSeverity.Error, true), null));
				}
			});
		}

		private static SchemaModel? Transform(GeneratorAttributeSyntaxContext ctx)
		{
			var structDecl = (StructDeclarationSyntax)ctx.TargetNode;
			var typeSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
			if (!typeSymbol.DeclaringSyntaxReferences.Any(s => s.GetSyntax() is StructDeclarationSyntax sd && sd.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))))
			{
				return null;
			}
			if (typeSymbol.IsGenericType || (typeSymbol.ContainingType != null && typeSymbol.ContainingType.IsGenericType))
			{
				return null;
			}

			// Defaults
			bool soa = true; int initialCapacity = 256; ushort poolId = 0; bool threadSafe = false; int resetPolicy = 0; bool generateStableId = true; byte refCounting = 0;
			foreach (var attr in typeSymbol.GetAttributes())
			{
				if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Pooled.PooledAttribute")
				{
					foreach (var kvp in attr.NamedArguments)
					{
						var name = kvp.Key;
						var value = kvp.Value;
						switch (name)
						{
							case "SoA": soa = (bool)value.Value!; break;
							case "InitialCapacity": initialCapacity = (int)value.Value!; break;
							case "PoolId": poolId = (ushort)value.Value!; break;
							case "ThreadSafe": threadSafe = (bool)value.Value!; break;
							case "ResetPolicy": resetPolicy = (int)value.Value!; break;
							case "GenerateStableId": generateStableId = (bool)value.Value!; break;
							case "RefCounting":
							{
								var vv = value.Value;
								if (vv is int iv) refCounting = (byte)iv;
								else if (vv is byte bv) refCounting = bv;
								else refCounting = 0;
								break;
							}
						}
					}
				}
			}

			var model = new SchemaModel(typeSymbol, soa, initialCapacity, poolId, threadSafe, resetPolicy, generateStableId, refCounting);

			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var m in typeSymbol.GetMembers().OfType<IFieldSymbol>())
			{
				var colAttr = m.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Pooled.ColumnAttribute");
				if (colAttr == null) continue;
				var name = m.Name;
				if (!names.Add(name))
				{
					continue;
				}
				bool inline = false, readOnly = false, noSerialize = false;
				byte storage = 0;
				foreach (var kvp in colAttr.NamedArguments)
				{
					var aname = kvp.Key;
					var aval = kvp.Value;
					switch (aname)
					{
						case "Inline": inline = (bool)aval.Value!; break;
						case "ReadOnly": readOnly = (bool)aval.Value!; break;
						case "NoSerialize": noSerialize = (bool)aval.Value!; break;
						case "Storage":
						{
							var vv = aval.Value;
							if (vv is int iv) storage = (byte)iv;
							else if (vv is byte bv) storage = bv;
							else if (vv is short sv) storage = (byte)sv;
							else if (vv is ushort usv) storage = (byte)usv;
							else if (vv is long lv) storage = (byte)lv;
							else if (vv is ulong ulv) storage = (byte)ulv;
							else storage = 0;
							break;
						}
					}
				}
				model.Columns.Add(new ColumnInfo(name, m.Type, inline, readOnly, noSerialize, storage));
			}

			return model;
		}
	}
}


