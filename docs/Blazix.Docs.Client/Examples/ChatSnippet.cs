namespace Blazix.Docs.Examples;

/// <summary>The code the code-block examples show.</summary>
internal static class ChatSnippet
{
    public const string Component =
        """
        "use client"

        export function Chat() {
          const [input, setInput] = React.useState("")
          const { messages, sendMessage, status } = useChat()

          return (
            <div className="flex flex-col gap-4">
              {messages.map((message) => (
                <div key={message.id}>
                  {message.parts.map((part, index) =>
                    part.type === "text" ? <p key={index}>{part.text}</p> : null
                  )}
                </div>
              ))}
              <form
                onSubmit={(event) => {
                  event.preventDefault()
                  sendMessage({ text: input })
                  setInput("")
                }}
              >
                <Input value={input} onChange={(event) => setInput(event.target.value)} />
                <Button disabled={status !== "ready"}>Send</Button>
              </form>
            </div>
          )
        }
        """;

    public const string Route =
        """
        import { streamText } from "ai"

        export async function POST(request: Request) {
          const { messages } = await request.json()

          const result = streamText({
            model: "anthropic/claude-sonnet-4.5",
            messages,
          })

          return result.toUIMessageStreamResponse()
        }
        """;

    public const string ShortRoute =
        """
        import { streamText } from "ai"

        export async function POST(request: Request) {
          const { messages } = await request.json()
          const result = streamText({ model: "anthropic/claude-sonnet-4.5", messages })
          return result.toUIMessageStreamResponse()
        }
        """;
}
