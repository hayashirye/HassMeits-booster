#include <flutter/dart_project.h>
#include <flutter/flutter_view_controller.h>
#include <windows.h>

#include "flutter_window.h"
#include "utils.h"

int APIENTRY wWinMain(_In_ HINSTANCE instance, _In_opt_ HINSTANCE prev,
                      _In_ wchar_t *command_line, _In_ int show_command) {
  // Attach to console when present (e.g., 'flutter run') or create a
  // new console when running with a debugger.
  if (!::AttachConsole(ATTACH_PARENT_PROCESS) && ::IsDebuggerPresent()) {
    CreateAndAttachConsole();
  }

  // Initialize COM, so that it is available for use in the library and/or
  // plugins.
  ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

  flutter::DartProject project(L"data");

  std::vector<std::string> command_line_arguments =
      GetCommandLineArguments();

  project.set_dart_entrypoint_arguments(std::move(command_line_arguments));

  FlutterWindow window(project);
  Win32Window::Point origin(10, 10);
  Win32Window::Size size(1280, 720);
  // Window title. This is what the taskbar and Alt+Tab actually show --
  // MaterialApp.title on the Dart side does NOT reach the native window
  // (it only feeds the Android recents list).
  //
  // Written with \uXXXX escapes on purpose: the source stays pure ASCII, so it
  // does not depend on MSVC's /utf-8 flag or on a source BOM. Putting the
  // literal characters here instead triggers C4819 ("character cannot be
  // represented in the current code page (936)") because MSVC reads a BOM-less
  // file as GBK -- and this project builds with warnings-as-errors, so it
  // fails the build outright.
  //
  // The text itself matches the C# build (one\Ui.cs:1035  Text = "...").
  if (!window.Create(L"\u6E38\u620F CPU \u9AD8\u9891\u4F18\u5316\u5668",
                     origin, size)) {
    return EXIT_FAILURE;
  }
  window.SetQuitOnClose(true);

  ::MSG msg;
  while (::GetMessage(&msg, nullptr, 0, 0)) {
    ::TranslateMessage(&msg);
    ::DispatchMessage(&msg);
  }

  ::CoUninitialize();
  return EXIT_SUCCESS;
}
