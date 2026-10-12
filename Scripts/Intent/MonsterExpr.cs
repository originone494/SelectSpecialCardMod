using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SelectSpecialCardMod.Scripts.Intent;

// 「属性表达式」求值器：把静态表里抓下来的表达式，在**这一只怪的实例**上算出来。
//
// 支持：整数字面量、怪物实例上的属性/字段（含 private）、'+ - * /' 与括号、
//      以及 `Players.Count` 这类人数项（替换成当前玩家人数）。
// 目的：覆盖静态表按字面量抓不到的那批值——升阶分支、随战斗变化的动态属性、随人数倍率，
//      都不需要先观测一次。
//
// 求不出来（语法不支持、属性不存在等）一律返回 0，调用方会退回占位值。
internal static class MonsterExpr
{
    private static readonly Dictionary<(Type, string), MemberInfo?> MemberCache = new();

    internal static int Eval(string? expr, Creature? source)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0;
        try
        {
            string text = Substitute(expr, source);
            int index = 0;
            int value = ParseSum(text, source, ref index);
            SkipSpaces(text, ref index);
            if (index < text.Length) return 0;   // 没解析完 → 语法不支持，当没有
            return Math.Max(0, value);
        }
        catch
        {
            return 0;
        }
    }

    // 人数项：把常见的写法替换成具体数字（其余不认识的部分会走反射或直接失败）
    private static string Substitute(string expr, Creature? source)
    {
        int players = source?.CombatState?.Players.Count ?? 1;
        return expr
            .Replace("base.CombatState.Players.Count", players.ToString(CultureInfo.InvariantCulture))
            .Replace("CombatState.Players.Count", players.ToString(CultureInfo.InvariantCulture))
            .Replace("base.CombatState.PlayerCreatures.Count", players.ToString(CultureInfo.InvariantCulture))
            .Replace("PlayerCreatures.Count", players.ToString(CultureInfo.InvariantCulture));
    }

    // sum := term (('+' | '-') term)*
    private static int ParseSum(string s, Creature? source, ref int i)
    {
        int value = ParseProduct(s, source, ref i);
        while (true)
        {
            SkipSpaces(s, ref i);
            if (i >= s.Length) return value;
            char op = s[i];
            if (op != '+' && op != '-') return value;
            i++;
            int rhs = ParseProduct(s, source, ref i);
            value = op == '+' ? value + rhs : value - rhs;
        }
    }

    // product := factor (('*' | '/') factor)*
    private static int ParseProduct(string s, Creature? source, ref int i)
    {
        int value = ParseFactor(s, source, ref i);
        while (true)
        {
            SkipSpaces(s, ref i);
            if (i >= s.Length) return value;
            char op = s[i];
            if (op != '*' && op != '/') return value;
            i++;
            int rhs = ParseFactor(s, source, ref i);
            value = op == '*' ? value * rhs : (rhs == 0 ? 0 : value / rhs);
        }
    }

    // factor := '(' sum ')' | number | identifier
    private static int ParseFactor(string s, Creature? source, ref int i)
    {
        SkipSpaces(s, ref i);
        if (i >= s.Length) throw new FormatException("表达式意外结束");

        char ch = s[i];
        if (ch == '(')
        {
            i++;
            int value = ParseSum(s, source, ref i);
            SkipSpaces(s, ref i);
            if (i < s.Length && s[i] == ')') i++;
            return value;
        }
        if (ch == '-' || ch == '+')
        {
            i++;
            int value = ParseFactor(s, source, ref i);
            return ch == '-' ? -value : value;
        }
        if (char.IsDigit(ch))
        {
            int start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            return int.Parse(s.AsSpan(start, i - start), CultureInfo.InvariantCulture);
        }
        if (char.IsLetter(ch) || ch == '_')
        {
            int start = i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            return ReadMember(source, s.Substring(start, i - start));
        }
        throw new FormatException($"无法解析的字符 '{ch}'");
    }

    private static void SkipSpaces(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    // 在怪物实例上取属性/字段（含 private，沿继承链找）
    private static int ReadMember(Creature? source, string name)
    {
        var monster = source?.Monster;
        if (monster == null) return 0;
        var member = FindMember(monster.GetType(), name);
        object? raw = member switch
        {
            PropertyInfo property => property.GetValue(monster),
            FieldInfo field => field.GetValue(monster),
            _ => null,
        };
        if (raw == null) return 0;
        return (int)Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        var key = (type, name);
        if (MemberCache.TryGetValue(key, out var cached)) return cached;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
                                   | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        MemberInfo? found = null;
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            var property = t.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                found = property;
                break;
            }
            var field = t.GetField(name, flags);
            if (field != null)
            {
                found = field;
                break;
            }
        }
        MemberCache[key] = found;
        return found;
    }
}
