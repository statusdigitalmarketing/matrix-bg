// Charset generator copied VERBATIM from matrix-bg.swift (MatrixView.charset).
// Its output is diffed against the shipped C build's charset dump by
// `make sim-test`; any divergence between the two ports fails the build.
import Foundation

var c: [String] = []
for v in 33...126 { c.append(String(UnicodeScalar(v)!)) }
for v in 0xFF66...0xFF9D { c.append(String(UnicodeScalar(v)!)) }
for s in c { print(String(format: "U+%04X", s.unicodeScalars.first!.value)) }
