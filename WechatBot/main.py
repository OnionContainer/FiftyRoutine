import os
import asyncio

from aibot import WSClient, WSClientOptions, generate_req_id

from botinfo import BOT_ID, BOT_SECRET



async def main():
    client = WSClient(WSClientOptions(
        bot_id=BOT_ID,
        secret=BOT_SECRET,
    ))

    @client.on("message.text")
    async def handle_message(frame):
        content = frame.get("body", {}).get("text", {}).get("content", "")
        print(f"收到消息：{content}")
        stream_id = generate_req_id("stream")
        await client.reply_stream(frame, stream_id, "蛋炒饭", True)

    await client.connect()

    # 保持程序运行
    await asyncio.Event().wait()


if __name__ == "__main__":
    asyncio.run(main())