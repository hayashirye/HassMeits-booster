// 数据表 —— 体检页的电源表 / 后台 CPU 表、游戏专项页的配置表都用它。
//
// ★ 逐行生长：一个 AnimationController 从 0 跑到 1（460ms），每一行按自己的
//   序号从总进度里切一段（stagger），所以行与行之间是错开的，但最后一行也
//   恰好在 460ms 归位 —— 不管有多少行。
//
// ★ 行悬停：鼠标划过时那一行淡淡提亮，120ms 淡入淡出。
//   Material You 管这个叫「状态层」（state layer）：在表面色上叠一层
//   onSurface，悬停 8%、按下 10%。下面用的就是这个数。
//
// ★ M3 里没有「数据表」这个组件（Material 的 Data Table 还是 M2 时代的），
//   所以这张表继续自绘，但每一处颜色都改从 ColorScheme 取 —— 这样换种子色
//   时表头、隔行、悬停、分隔线会跟着一起变，而不是只有按钮变。
import 'package:flutter/material.dart';

import '../theme.dart';

/// 一列的定義
class GridCol {
  final String title;
  final int flex;
  final bool right; // 右对齐（数字列）
  final bool badge; // 这一列画成彩色药丸
  final bool bar; // 这一列画成比例条
  const GridCol(
    this.title, {
    this.flex = 1,
    this.right = false,
    this.badge = false,
    this.bar = false,
  });
}

/// 一行数据
class GridRow {
  final List<String> cells;
  final int kind; // 0 绿 / 1 红 / 2 灰 / 3 琥珀
  final double? bar; // bar 列的百分比（0-100）
  final String tooltip;
  const GridRow(this.cells, {this.kind = 2, this.bar, this.tooltip = ''});
}

class DataGrid extends StatefulWidget {
  final List<GridCol> cols;
  final List<GridRow> rows;
  final double rowHeight;
  final double headHeight;

  /// 每次 rows 变了就把它 +1，表格会重新播放一次生长动画
  final int playToken;

  const DataGrid({
    super.key,
    required this.cols,
    required this.rows,
    this.rowHeight = 30,
    this.headHeight = 34,
    this.playToken = 0,
  });

  @override
  State<DataGrid> createState() => _DataGridState();
}

class _DataGridState extends State<DataGrid>
    with SingleTickerProviderStateMixin {
  late final AnimationController _c;
  int _hoverRow = -1;

  @override
  void initState() {
    super.initState();
    _c = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: T.dTableGrow),
    )..forward();
  }

  @override
  void didUpdateWidget(covariant DataGrid old) {
    super.didUpdateWidget(old);
    if (old.playToken != widget.playToken) {
      _c.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  /// 第 i 行在这一刻的进度（已过 ease-out）
  double _rowProgress(int i, int count) {
    if (count <= 1) return Curves.easeOut.transform(_c.value);
    final stagger = 0.5 / count;
    final span = (1.0 - stagger * (count - 1)).clamp(0.05, 1.0);
    final raw = ((_c.value - i * stagger) / span).clamp(0.0, 1.0);
    return Curves.easeOut.transform(raw);
  }

  Color _kindColor(int kind) {
    switch (kind) {
      case 0:
        return T.ok;
      case 1:
        return T.bad;
      case 3:
        return T.warn;
      default:
        return T.mute;
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _header(cs),
        Expanded(
          child: widget.rows.isEmpty
              ? Center(
                  child: Text(
                    '还没有数据',
                    style: TextStyle(color: cs.outline, fontSize: T.fsSmall),
                  ),
                )
              : Scrollbar(
                  thickness: 4,
                  radius: const Radius.circular(2),
                  child: ListView.builder(
                    padding: EdgeInsets.zero,
                    itemCount: widget.rows.length,
                    itemExtent: widget.rowHeight,
                    itemBuilder: (_, i) => _row(i, cs),
                  ),
                ),
        ),
      ],
    );
  }

  Widget _header(ColorScheme cs) {
    return Container(
      height: widget.headHeight,
      decoration: BoxDecoration(
        // M3 的表头用比卡片再亮一档的表面
        color: cs.surfaceContainerHigh,
        border: Border(bottom: BorderSide(color: cs.outlineVariant)),
      ),
      child: Row(
        children: [
          for (var i = 0; i < widget.cols.length; i++)
            Expanded(
              flex: widget.cols[i].flex,
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 10),
                child: Align(
                  alignment: widget.cols[i].badge
                      ? Alignment.center
                      : (widget.cols[i].right
                          ? Alignment.centerRight
                          : Alignment.centerLeft),
                  child: Text(
                    widget.cols[i].title,
                    style: TextStyle(
                      color: cs.onSurfaceVariant,
                      fontSize: T.fsTiny,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ),
            ),
          const SizedBox(width: 10), // 给滚动条让位
        ],
      ),
    );
  }

  Widget _row(int i, ColorScheme cs) {
    final row = widget.rows[i];
    final p = _rowProgress(i, widget.rows.length);
    final hot = _hoverRow == i;

    // M3 的状态层：悬停 8% 的 onSurface 叠在表面上。
    // 隔行那层更淡（3%），只为了在密集表格里帮着横向读。
    final base = i.isEven
        ? cs.onSurface.withValues(alpha: 0.03)
        : Colors.transparent;
    final tint = hot ? cs.onSurface.withValues(alpha: 0.08) : base;

    return Opacity(
      opacity: p.clamp(0.0, 1.0),
      child: Transform.translate(
        offset: Offset(0, (1 - p) * 9), // 从下方 9px 滑上来
        child: MouseRegion(
          onEnter: (_) => setState(() => _hoverRow = i),
          onExit: (_) => setState(() => _hoverRow = -1),
          child: Tooltip(
            message: row.tooltip.isEmpty ? row.cells.join('   ') : row.tooltip,
            waitDuration: const Duration(milliseconds: 400),
            child: Stack(
              children: [
                AnimatedContainer(
                  duration: const Duration(milliseconds: T.dRowHover),
                  decoration: BoxDecoration(
                    color: tint,
                    border: Border(
                      bottom: BorderSide(
                        color: cs.outlineVariant.withValues(alpha: 0.5),
                      ),
                    ),
                  ),
                ),
                Positioned.fill(
                  child: Row(
                    children: [
                      for (var c = 0; c < widget.cols.length; c++)
                        Expanded(
                          flex: widget.cols[c].flex,
                          child: Padding(
                            padding: const EdgeInsets.symmetric(horizontal: 10),
                            child: _cell(
                              widget.cols[c],
                              c < row.cells.length ? row.cells[c] : '',
                              row,
                              cs,
                            ),
                          ),
                        ),
                      const SizedBox(width: 10),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _cell(GridCol col, String text, GridRow row, ColorScheme cs) {
    // 药丸列
    if (col.badge) {
      if (text.isEmpty) return const SizedBox.shrink();
      final color = _kindColor(row.kind);
      return Align(
        alignment: Alignment.center,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 2),
          decoration: BoxDecoration(
            color: color.withValues(alpha: 0.16),
            borderRadius: BorderRadius.circular(100), // 全圆角（M3 chip）
            border: Border.all(color: color.withValues(alpha: 0.5)),
          ),
          child: Text(
            text,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              color: color,
              fontSize: T.fsTiny,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
      );
    }

    // 比例条列
    if (col.bar) {
      final pct = row.bar ?? 0;
      final color = T.pctColor(pct);
      return Row(
        children: [
          SizedBox(
            width: 42,
            child: Text(
              text,
              textAlign: TextAlign.right,
              style: TextStyle(
                color: color,
                fontSize: T.fsTiny + 0.5,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Align(
              alignment: Alignment.centerLeft,
              child: FractionallySizedBox(
                // 条本身的长度按百分比，再乘上生长进度 p
                widthFactor: ((pct / 100).clamp(0.03, 1.0) * _c.value)
                    .clamp(0.0, 1.0),
                child: Container(
                  height: 6,
                  decoration: BoxDecoration(
                    color: color,
                    borderRadius: BorderRadius.circular(3),
                  ),
                ),
              ),
            ),
          ),
        ],
      );
    }

    // 普通文本
    return Align(
      alignment: col.right ? Alignment.centerRight : Alignment.centerLeft,
      child: Text(
        text,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        textAlign: col.right ? TextAlign.right : TextAlign.left,
        style: TextStyle(color: cs.onSurface, fontSize: T.fsSmall + 0.5),
      ),
    );
  }
}
