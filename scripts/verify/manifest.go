package main

import (
	"encoding/json"
	"fmt"
	"go/ast"
	"go/parser"
	"go/printer"
	"go/token"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
)

type Field struct {
	GoName   string `json:"goName"`
	JSONName string `json:"jsonName,omitempty"`
	Type     string `json:"type"`
	Embedded bool   `json:"embedded,omitempty"`
	Ignored  bool   `json:"ignored,omitempty"`
	Source   string `json:"source,omitempty"`
}

type Type struct {
	Name   string  `json:"name"`
	Kind   string  `json:"kind"`
	File   string  `json:"file"`
	Fields []Field `json:"fields,omitempty"`
	Under  string  `json:"underlying,omitempty"`
}

type Method struct {
	Name    string   `json:"name"`
	Params  []string `json:"params,omitempty"`
	Results []string `json:"results,omitempty"`
	File    string   `json:"file"`
}

type Interface struct {
	Name    string   `json:"name"`
	Methods []string `json:"methods"`
}

type Manifest struct {
	Types      []Type                 `json:"types"`
	Methods    map[string][]Method    `json:"methods"`
	Interfaces map[string]Interface   `json:"interfaces"`
}

func exprString(fset *token.FileSet, f ast.Expr) string {
	var b strings.Builder
	_ = printer.Fprint(&b, fset, f)
	return b.String()
}

func jsonTag(tag *ast.BasicLit) (name string, ok bool) {
	if tag == nil {
		return "", false
	}

	raw, err := strconv.Unquote(tag.Value)
	if err != nil {
		return "", false
	}

	const prefix = `json:"`
	start := strings.Index(raw, prefix)
	if start < 0 {
		return "", false
	}

	rest := raw[start+len(prefix):]
	end := strings.IndexByte(rest, '"')
	if end < 0 {
		return "", false
	}

	value := rest[:end]
	parts := strings.Split(value, ",")
	if parts[0] == "-" {
		return "", false
	}

	return parts[0], true
}

func main() {
	if len(os.Args) < 3 {
		fmt.Fprintln(os.Stderr, "usage: manifest <api-dir> <client-dir>")
		os.Exit(1)
	}

	apiDir := os.Args[1]
	clientDir := os.Args[2]
	fset := token.NewFileSet()

	apiFiles, err := parser.ParseDir(fset, apiDir, func(fi os.FileInfo) bool {
		return !strings.HasSuffix(fi.Name(), "_test.go")
	}, parser.ParseComments)
	if err != nil {
		panic(err)
	}

	structs := map[string]*ast.StructType{}
	typeDecls := map[string]*ast.TypeSpec{}
	typeFiles := map[string]string{}

	for _, pkg := range apiFiles {
		for fileName, file := range pkg.Files {
			for _, decl := range file.Decls {
				gen, ok := decl.(*ast.GenDecl)
				if !ok || gen.Tok != token.TYPE {
					continue
				}

				for _, spec := range gen.Specs {
					ts := spec.(*ast.TypeSpec)
					typeDecls[ts.Name.Name] = ts
					typeFiles[ts.Name.Name] = filepath.Base(fileName)
					if st, ok := ts.Type.(*ast.StructType); ok {
						structs[ts.Name.Name] = st
					}
				}
			}
		}
	}

	var flatten func(name string, seen map[string]bool) []Field
	flatten = func(name string, seen map[string]bool) []Field {
		st, ok := structs[name]
		if !ok || seen[name] {
			return nil
		}

		seen[name] = true
		defer delete(seen, name)

		var fields []Field
		for _, field := range st.Fields.List {
			if len(field.Names) == 0 {
				typ := exprString(fset, field.Type)
				base := strings.TrimPrefix(typ, "*")
				if embeddedStructs, isStruct := structs[base]; isStruct && embeddedStructs != nil {
					fields = append(fields, flatten(base, seen)...)
					continue
				}

				jsonName, _ := jsonTag(field.Tag)
				fields = append(fields, Field{
					GoName:   base,
					JSONName: jsonName,
					Type:     typ,
					Embedded: true,
					Source:   name,
				})
				continue
			}

			jsonName, hasJSON := jsonTag(field.Tag)
			ignored := false
			if field.Tag != nil {
				raw, _ := strconv.Unquote(field.Tag.Value)
				if strings.Contains(raw, `json:"-"`) {
					ignored = true
				}
			}

			if !hasJSON && !ignored {
				jsonName = field.Names[0].Name
			}

			for _, n := range field.Names {
				fields = append(fields, Field{
					GoName:   n.Name,
					JSONName: jsonName,
					Type:     exprString(fset, field.Type),
					Ignored:  ignored,
					Source:   name,
				})
			}
		}

		return fields
	}

	names := make([]string, 0, len(typeDecls))
	for name := range typeDecls {
		names = append(names, name)
	}
	sort.Strings(names)

	manifest := Manifest{
		Methods:    map[string][]Method{},
		Interfaces: map[string]Interface{},
	}

	for _, name := range names {
		ts := typeDecls[name]
		t := Type{Name: name, File: typeFiles[name]}
		switch typ := ts.Type.(type) {
		case *ast.StructType:
			t.Kind = "struct"
			t.Fields = flatten(name, map[string]bool{})
		case *ast.InterfaceType:
			t.Kind = "interface"
		default:
			t.Kind = "defined"
			t.Under = exprString(fset, typ)
		}

		manifest.Types = append(manifest.Types, t)
	}

	clientFiles, err := parser.ParseDir(fset, clientDir, func(fi os.FileInfo) bool {
		return !strings.HasSuffix(fi.Name(), "_test.go")
	}, parser.ParseComments)
	if err != nil {
		panic(err)
	}

	for _, pkg := range clientFiles {
		for fileName, file := range pkg.Files {
			for _, decl := range file.Decls {
				switch d := decl.(type) {
				case *ast.FuncDecl:
					if d.Recv == nil || len(d.Recv.List) == 0 {
						continue
					}

					recv := strings.TrimPrefix(exprString(fset, d.Recv.List[0].Type), "*")
					if !d.Name.IsExported() {
						continue
					}

					m := Method{Name: d.Name.Name, File: filepath.Base(fileName)}
					if d.Type.Params != nil {
						for _, p := range d.Type.Params.List {
							m.Params = append(m.Params, exprString(fset, p.Type))
						}
					}
					if d.Type.Results != nil {
						for _, r := range d.Type.Results.List {
							m.Results = append(m.Results, exprString(fset, r.Type))
						}
					}
					manifest.Methods[recv] = append(manifest.Methods[recv], m)

				case *ast.GenDecl:
					if d.Tok != token.TYPE {
						continue
					}
					for _, spec := range d.Specs {
						ts := spec.(*ast.TypeSpec)
						it, ok := ts.Type.(*ast.InterfaceType)
						if !ok || it.Methods == nil {
							continue
						}
						var methods []string
						for _, f := range it.Methods.List {
							if len(f.Names) == 0 {
								continue
							}
							methods = append(methods, f.Names[0].Name)
						}
						manifest.Interfaces[ts.Name.Name] = Interface{Name: ts.Name.Name, Methods: methods}
					}
				}
			}
		}
	}

	for k := range manifest.Methods {
		sort.Slice(manifest.Methods[k], func(i, j int) bool {
			return manifest.Methods[k][i].Name < manifest.Methods[k][j].Name
		})
	}

	enc := json.NewEncoder(os.Stdout)
	enc.SetIndent("", "  ")
	if err := enc.Encode(manifest); err != nil {
		panic(err)
	}
}
