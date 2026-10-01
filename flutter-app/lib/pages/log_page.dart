// 日志页 —— 直接读 boost-log.txt。
import 'package:flutter/material.dart';

import '../app_state.dart';
import '../engine.dart';
import '../theme.dart';
import '../widgets/app_card.dart';
import '../widgets/press_button.dart';

class LogPage extends StatelessWidget {
  final AppState st;
  const LogPage(this.st, {super.key});

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: st,
      builder: (_, __) => Padding(
        padding: const EdgeInsets.fromLTRB(18, 16, 18, 18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                const CardTitle('运行日志'),
                const Spacer(),
                PressButton(
                  label: '刷新',
                  onTap: st.refreshLog,
                  padding:
                      const EdgeInsets.symmetric(horizontal: 18, vertical: 11),
                ),
                const SizedBox(width: 10),
                PressButton(
                  label: '打开所在文件夹',
                  onTap: () => Engine.i.revealLog(),
                  padding:
                      const EdgeInsets.symmetric(horizontal: 18, vertical: 11),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Expanded(
              child: AppCard(
                padding: const EdgeInsets.all(4),
                child: Container(
                  decoration: BoxDecoration(
                    color: const Color(0xFF0A0C10),
                    borderRadius: BorderRadius.circular(9),
                    border: Border.all(color: T.line),
                  ),
                  child: st.logLines.isEmpty
                      ? Center(
                          child: Text(
                            '日志还是空的。\n优化或启动守护之后就会有记录。',
                            textAlign: TextAlign.center,
                            style: TextStyle(
                                color: T.fgFaint,
                                fontSize: T.fsSmall,
                                height: 1.7),
                          ),
                        )
                      : Scrollbar(
                          thickness: 4,
                          radius: const Radius.circular(2),
                          child: SingleChildScrollView(
                            padding: const EdgeInsets.all(12),
                            child: SelectableText(
                              st.logLines.join('\n'),
                              style: TextStyle(
                                color: T.fgDim,
                                fontSize: T.fsTiny,
                                height: 1.75,
                                fontFamily: 'Consolas',
                              ),
                            ),
                          ),
                        ),
                ),
              ),
            ),
            const SizedBox(height: 8),
            Text(
              Engine.i.logPath,
              style: TextStyle(
                color: T.fgFaint,
                fontSize: T.fsTiny,
                fontFamily: 'Consolas',
              ),
            ),
          ],
        ),
      ),
    );
  }
}
