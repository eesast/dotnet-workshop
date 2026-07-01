import os
import sys

for i in range(1, 31):
    datestr = f"202607{i:02d}"
    count = 40000 + int(datestr) - 20260601
    start_time = f'2026-07-{i:02d}T08:00:00Z'

    class_name_op = f"-C Dataset{datestr}"
    namespace_op = f"-n TestUtils.dataset.multiple_logs"
    csv_op = f"--csv multiple-logs/{datestr}.log"
    cs_op = f"--cs gen_logs/Dataset{datestr}.cs"
    cnt_op = f"-c {count}"
    start_time_op = f"-s {start_time}"

    python_exec = sys.executable
    os.system(f"{python_exec} gen.py {class_name_op} {namespace_op} {csv_op} {cs_op} {cnt_op} {start_time_op}")
